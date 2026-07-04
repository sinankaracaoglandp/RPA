namespace RPA.Infrastructure.Activities.Api;

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;
using BusinessException = RPA.Domain.Exceptions.BusinessException;
using SystemException = RPA.Domain.Exceptions.SystemException;

/// <summary>
/// "Api.HttpRequest" aktivitesi — GET/POST/PUT/DELETE HTTP istekleri, auth şablonları
/// (Basic/Bearer/API-key) ve Polly retry + circuit-breaker dayanıklılık politikası.
/// Spec Bölüm 5.3 (API), Bölüm 6 (istisna sınıflandırma: HTTP 5xx → System, 4xx → Business).
/// </summary>
public sealed class ApiHttpActivity : IActivity
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ApiHttpActivity(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var method = context.GetVariable<string>("method");
        if (string.IsNullOrWhiteSpace(method))
        {
            method = "GET";
        }

        var url = context.GetVariable<string>("url");
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new SystemException("Api.HttpRequest: 'url' parametresi zorunlu.");
        }

        var headersJson = context.GetVariable<string>("headers");
        var body = context.GetVariable<string>("body");
        var authType = context.GetVariable<string>("authType");
        var credentialName = context.GetVariable<string>("credentialName");

        var apiKeyHeaderName = context.GetVariable<string>("apiKeyHeaderName");
        if (string.IsNullOrWhiteSpace(apiKeyHeaderName))
        {
            apiKeyHeaderName = "X-Api-Key";
        }

        var timeoutSeconds = context.GetVariable<int?>("timeoutSeconds") ?? 30;
        var retryCount = context.GetVariable<int?>("retryCount") ?? 3;
        var circuitBreakerFailureThreshold = context.GetVariable<int?>("circuitBreakerFailureThreshold") ?? 5;
        var circuitBreakerDurationSeconds = context.GetVariable<int?>("circuitBreakerDurationSeconds") ?? 30;

        string? authValue = null;
        if (!string.IsNullOrWhiteSpace(authType) &&
            !string.Equals(authType, "None", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(credentialName))
        {
            authValue = await context.GetCredentialAsync(credentialName);
        }

        var client = _httpClientFactory.CreateClient("Api.HttpRequest");
        var pipeline = BuildPipeline(retryCount, circuitBreakerFailureThreshold, circuitBreakerDurationSeconds, context);

        HttpResponseMessage response;
        try
        {
            response = await pipeline.ExecuteAsync(async ct =>
            {
                using var request = BuildRequest(method!, url!, headersJson, body, authType, authValue, apiKeyHeaderName!);
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                return await client.SendAsync(request, linkedCts.Token).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        catch (BrokenCircuitException ex)
        {
            throw new SystemException(
                $"Api.HttpRequest: circuit breaker açık, istek engellendi ({url}).", ex);
        }
        catch (OperationCanceledException ex)
        {
            throw new SystemException($"Api.HttpRequest: zaman aşımı ({timeoutSeconds}s) — {url}.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new SystemException($"Api.HttpRequest: bağlantı hatası — {url}.", ex);
        }

        var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var statusCode = (int)response.StatusCode;

        context.Log($"Api.HttpRequest {method} {url} -> {statusCode}");

        var responseHeaders = response.Headers
            .Concat(response.Content.Headers)
            .ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);

        var outputs = new Dictionary<string, object?>
        {
            ["statusCode"] = statusCode,
            ["responseBody"] = responseBody,
            ["responseHeaders"] = JsonSerializer.Serialize(responseHeaders),
            ["isSuccess"] = response.IsSuccessStatusCode,
        };

        if (statusCode >= 500)
        {
            throw new SystemException(
                $"Api.HttpRequest: sunucu hatası HTTP {statusCode} ({url}). Body: {Truncate(responseBody)}");
        }

        if (statusCode >= 400)
        {
            throw new BusinessException(
                $"Api.HttpRequest: istemci hatası HTTP {statusCode} ({url}). Body: {Truncate(responseBody)}");
        }

        return outputs;
    }

    private static HttpRequestMessage BuildRequest(
        string method,
        string url,
        string? headersJson,
        string? body,
        string? authType,
        string? authValue,
        string apiKeyHeaderName)
    {
        var httpMethod = method.ToUpperInvariant() switch
        {
            "GET" => HttpMethod.Get,
            "POST" => HttpMethod.Post,
            "PUT" => HttpMethod.Put,
            "DELETE" => HttpMethod.Delete,
            "PATCH" => HttpMethod.Patch,
            _ => throw new SystemException($"Api.HttpRequest: desteklenmeyen HTTP metodu '{method}'."),
        };

        var request = new HttpRequestMessage(httpMethod, url);

        if (!string.IsNullOrWhiteSpace(body) &&
            (httpMethod == HttpMethod.Post || httpMethod == HttpMethod.Put || httpMethod == HttpMethod.Patch))
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        if (!string.IsNullOrWhiteSpace(headersJson))
        {
            using var doc = JsonDocument.Parse(headersJson);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var value = prop.Value.ValueKind == JsonValueKind.String
                    ? prop.Value.GetString()
                    : prop.Value.ToString();
                if (!request.Headers.TryAddWithoutValidation(prop.Name, value) && request.Content is not null)
                {
                    request.Content.Headers.TryAddWithoutValidation(prop.Name, value);
                }
            }
        }

        ApplyAuth(request, authType, authValue, apiKeyHeaderName);

        return request;
    }

    private static void ApplyAuth(HttpRequestMessage request, string? authType, string? authValue, string apiKeyHeaderName)
    {
        if (string.IsNullOrWhiteSpace(authType) || string.IsNullOrWhiteSpace(authValue))
        {
            return;
        }

        switch (authType.Trim().ToLowerInvariant())
        {
            case "basic":
                // authValue biçimi "kullanici:sifre" olmalı.
                var basicToken = Convert.ToBase64String(Encoding.UTF8.GetBytes(authValue));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicToken);
                break;
            case "bearer":
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authValue);
                break;
            case "apikey":
            case "api-key":
                request.Headers.TryAddWithoutValidation(apiKeyHeaderName, authValue);
                break;
            case "none":
                break;
            default:
                throw new SystemException($"Api.HttpRequest: bilinmeyen authType '{authType}'.");
        }
    }

    /// <summary>
    /// Retry (üstel geri çekilme, 5xx/bağlantı hatası) + circuit-breaker sarmalı politika.
    /// Spec Bölüm 5.2: SystemException için üstel geri çekilme; burada HTTP seviyesinde uygulanır.
    /// </summary>
    private static ResiliencePipeline<HttpResponseMessage> BuildPipeline(
        int retryCount,
        int circuitBreakerFailureThreshold,
        int circuitBreakerDurationSeconds,
        IActivityExecutionContext context)
    {
        var builder = new ResiliencePipelineBuilder<HttpResponseMessage>();

        builder.AddRetry(new RetryStrategyOptions<HttpResponseMessage>
        {
            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                .Handle<HttpRequestException>()
                .HandleResult(r => (int)r.StatusCode >= 500),
            MaxRetryAttempts = Math.Max(0, retryCount),
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromMilliseconds(200),
            UseJitter = true,
            OnRetry = args =>
            {
                context.Log(
                    $"Api.HttpRequest: yeniden deneme {args.AttemptNumber + 1}/{retryCount} " +
                    $"(sebep: {DescribeOutcome(args.Outcome)})",
                    RPA.Domain.Interfaces.LogLevel.Warning);
                return default;
            },
        });

        builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
        {
            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                .Handle<HttpRequestException>()
                .HandleResult(r => (int)r.StatusCode >= 500),
            FailureRatio = 1.0,
            MinimumThroughput = Math.Max(2, circuitBreakerFailureThreshold),
            SamplingDuration = TimeSpan.FromSeconds(Math.Max(1, circuitBreakerDurationSeconds)),
            BreakDuration = TimeSpan.FromSeconds(Math.Max(1, circuitBreakerDurationSeconds)),
            OnOpened = args =>
            {
                context.Log(
                    "Api.HttpRequest: circuit breaker açıldı — ardışık sunucu hataları eşiği aşıldı.",
                    RPA.Domain.Interfaces.LogLevel.Error);
                return default;
            },
        });

        return builder.Build();
    }

    private static string DescribeOutcome(Outcome<HttpResponseMessage> outcome)
    {
        if (outcome.Exception is not null)
        {
            return outcome.Exception.Message;
        }
        return outcome.Result is not null ? $"HTTP {(int)outcome.Result.StatusCode}" : "bilinmeyen";
    }

    private static string Truncate(string value, int maxLength = 500)
        => value.Length <= maxLength ? value : value[..maxLength] + "...";

    public ActivityMetadata GetMetadata() => new()
    {
        ActivityId = "Api.HttpRequest",
        DisplayName = "HTTP İsteği",
        Category = "API",
        Description = "HTTP isteği gönderir (GET/POST/PUT/DELETE); Basic/Bearer/API-key auth şablonları; Polly retry + circuit-breaker.",
        Inputs =
        {
            new ActivityParameter { Name = "method", Type = "string", Required = false, DefaultValue = "GET" },
            new ActivityParameter { Name = "url", Type = "string", Required = true },
            new ActivityParameter { Name = "headers", Type = "JSON", Required = false },
            new ActivityParameter { Name = "body", Type = "JSON", Required = false },
            new ActivityParameter { Name = "authType", Type = "string", Required = false, Description = "None | Basic | Bearer | ApiKey" },
            new ActivityParameter { Name = "credentialName", Type = "Credential", Required = false },
            new ActivityParameter { Name = "apiKeyHeaderName", Type = "string", Required = false, DefaultValue = "X-Api-Key" },
            new ActivityParameter { Name = "timeoutSeconds", Type = "int", Required = false, DefaultValue = 30 },
            new ActivityParameter { Name = "retryCount", Type = "int", Required = false, DefaultValue = 3 },
            new ActivityParameter { Name = "circuitBreakerFailureThreshold", Type = "int", Required = false, DefaultValue = 5 },
            new ActivityParameter { Name = "circuitBreakerDurationSeconds", Type = "int", Required = false, DefaultValue = 30 },
        },
        Outputs =
        {
            new ActivityParameter { Name = "statusCode", Type = "int", Required = false },
            new ActivityParameter { Name = "responseBody", Type = "JSON", Required = false },
            new ActivityParameter { Name = "responseHeaders", Type = "JSON", Required = false },
            new ActivityParameter { Name = "isSuccess", Type = "bool", Required = false },
        },
        ExceptionClassification = new ExceptionClassificationRule
        {
            Condition = "StatusCode>=500",
            Classification = RPA.Domain.Enums.ExceptionType.System,
        },
    };
}

namespace RPA.Infrastructure.Workflow.Activities.Api;

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;
using BusinessException = RPA.Domain.Exceptions.BusinessException;
using SystemException = RPA.Domain.Exceptions.SystemException;

public sealed class ApiHttpActivity : IActivity
{
    private readonly IHttpClientFactory _factory;

    public ApiHttpActivity(IHttpClientFactory factory) 
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext ctx)
    {
        var method = ctx.GetVariable<string>("method") ?? "GET";
        var url = ctx.GetVariable<string>("url");
        if (string.IsNullOrWhiteSpace(url)) 
            throw new SystemException("url required");

        var timeout = ctx.GetVariable<int?>("timeoutSeconds") ?? 30;
        var auth = ctx.GetVariable<string>("authType");
        var cred = ctx.GetVariable<string>("credentialName");

        string authVal = null;
        if (!string.IsNullOrWhiteSpace(auth) && !string.IsNullOrWhiteSpace(cred))
            authVal = await ctx.GetCredentialAsync(cred);

        var client = _factory.CreateClient("Api.HttpRequest");
        
        using var req = new HttpRequestMessage(
            method.ToUpperInvariant() switch { "POST" => HttpMethod.Post, "PUT" => HttpMethod.Put, "DELETE" => HttpMethod.Delete, _ => HttpMethod.Get },
            url);

        if (!string.IsNullOrWhiteSpace(auth) && !string.IsNullOrWhiteSpace(authVal))
        {
            if (auth.Equals("Bearer", StringComparison.OrdinalIgnoreCase))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authVal);
            else if (auth.Equals("Basic", StringComparison.OrdinalIgnoreCase))
            {
                var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(authVal));
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", b64);
            }
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
        var resp = await client.SendAsync(req, cts.Token);
        var code = (int)resp.StatusCode;
        var body = await resp.Content.ReadAsStringAsync();

        if (code >= 500) throw new SystemException($"HTTP {code}");
        if (code >= 400) throw new BusinessException($"HTTP {code}");

        return new() { ["statusCode"] = code, ["responseBody"] = body, ["isSuccess"] = true };
    }

    public ActivityMetadata GetMetadata() => new()
    {
        ActivityId = "Api.HttpRequest",
        DisplayName = "HTTP Request",
        Category = "API",
        Inputs = new() { new() { Name = "url", Type = "string", Required = true } },
        Outputs = new() { new() { Name = "statusCode", Type = "int" }, new() { Name = "responseBody", Type = "JSON" } },
    };
}

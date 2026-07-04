namespace RPA.Infrastructure.Vault;

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RPA.Domain.Interfaces;

/// <summary>
/// HashiCorp Vault (KV v2) REST API tabanlı ICredentialVault implementasyonu.
/// Spec Bölüm 5.5, 10 — on-premise HashiCorp Vault.
///
/// Değer Vault sunucusunda saklanır (sunucu tarafı at-rest şifreleme). Bu
/// implementasyon değeri plaintext olarak KV'ye yazar; okumada değer yeniden
/// yerel <see cref="SecureString"/> (DPAPI) içine sarılarak döner — böylece
/// bellekte ve loglarda daima maskeli kalır.
///
/// Endpoint'ler:
///   PUT/POST  v1/{mount}/data/{key}       — secret yaz (versiyonlu)
///   GET       v1/{mount}/data/{key}       — secret oku
///   DELETE    v1/{mount}/metadata/{key}   — tüm versiyonları sil
///   POST      v1/{mount}/metadata/{key}   — custom_metadata (tag) yaz
///   LIST      v1/{mount}/metadata?list=true — key listesi
/// </summary>
public sealed class HashiCorpVaultClient : ICredentialVault
{
    private readonly HttpClient _http;
    private readonly HashiCorpOptions _options;
    private readonly ILogger<HashiCorpVaultClient> _logger;

    public HashiCorpVaultClient(
        HttpClient http,
        IOptions<VaultOptions> options,
        ILogger<HashiCorpVaultClient> logger)
    {
        _http = http;
        _options = options.Value.HashiCorp;
        _logger = logger;

        if (_http.BaseAddress is null && !string.IsNullOrWhiteSpace(_options.Url))
        {
            _http.BaseAddress = new Uri(_options.Url.TrimEnd('/') + "/");
        }
        if (!string.IsNullOrWhiteSpace(_options.Token)
            && !_http.DefaultRequestHeaders.Contains("X-Vault-Token"))
        {
            _http.DefaultRequestHeaders.Add("X-Vault-Token", _options.Token);
        }
    }

    public async Task<SecureString> GetSecretAsync(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var response = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Get, DataPath(key)));

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new KeyNotFoundException($"Vault key bulunamadı: '{key}'.");
        }
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        var value = JObject.Parse(body)
            ["data"]?["data"]?["value"]?.Value<string>()
            ?? throw new InvalidDataException(
                $"Vault yanıtında 'value' alanı yok: '{key}'.");

        _logger.LogInformation("Vault secret alındı: {Key}", key);
        return new SecureString(value);
    }

    public async Task StoreSecretAsync(
        string key, SecureString secret, Dictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(secret);

        var payload = new JObject
        {
            ["data"] = new JObject { ["value"] = secret.Decrypt() },
        };

        var writeResponse = await SendWithRetryAsync(() =>
            new HttpRequestMessage(HttpMethod.Post, DataPath(key))
            {
                Content = JsonContent(payload),
            });
        writeResponse.EnsureSuccessStatusCode();

        if (metadata is { Count: > 0 })
        {
            var meta = new JObject
            {
                ["custom_metadata"] = JObject.FromObject(metadata),
            };
            var metaResponse = await SendWithRetryAsync(() =>
                new HttpRequestMessage(HttpMethod.Post, MetadataPath(key))
                {
                    Content = JsonContent(meta),
                });
            metaResponse.EnsureSuccessStatusCode();
        }

        // Değer asla loglanmaz — yalnızca key ve tag anahtarları.
        _logger.LogInformation("Vault secret saklandı: {Key}, tags: {Tags}",
            key, metadata is null ? string.Empty : string.Join(",", metadata.Keys));
    }

    public async Task DeleteSecretAsync(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var response = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Delete, MetadataPath(key)));

        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }
        _logger.LogInformation("Vault secret silindi: {Key}", key);
    }

    public async Task<bool> ExistsAsync(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var response = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Get, DataPath(key)));

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
        response.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<IEnumerable<string>> ListSecretsByTagAsync(string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        // KV v2 list: GET v1/{mount}/metadata?list=true
        var listResponse = await SendWithRetryAsync(() =>
            new HttpRequestMessage(HttpMethod.Get,
                $"v1/{_options.Mount}/metadata?list=true"));

        if (listResponse.StatusCode == HttpStatusCode.NotFound)
        {
            return Array.Empty<string>();
        }
        listResponse.EnsureSuccessStatusCode();

        var listBody = await listResponse.Content.ReadAsStringAsync();
        var keys = JObject.Parse(listBody)["data"]?["keys"]?.Values<string>()
            .Where(k => k is not null).Select(k => k!).ToList()
            ?? new List<string>();

        var matches = new List<string>();
        foreach (var key in keys)
        {
            var metaResponse = await SendWithRetryAsync(
                () => new HttpRequestMessage(HttpMethod.Get, MetadataPath(key)));
            if (!metaResponse.IsSuccessStatusCode)
            {
                continue;
            }

            var metaBody = await metaResponse.Content.ReadAsStringAsync();
            var custom = JObject.Parse(metaBody)["data"]?["custom_metadata"] as JObject;
            if (custom is not null && MatchesTag(custom, tag))
            {
                matches.Add(key);
            }
        }

        return matches;
    }

    private static bool MatchesTag(JObject custom, string tag)
    {
        var eq = tag.IndexOf('=');
        if (eq >= 0)
        {
            var k = tag[..eq];
            var v = tag[(eq + 1)..];
            return custom.TryGetValue(k, StringComparison.OrdinalIgnoreCase, out var actual)
                && string.Equals(actual.Value<string>(), v, StringComparison.OrdinalIgnoreCase);
        }

        return custom.Properties().Any(p =>
            string.Equals(p.Name, tag, StringComparison.OrdinalIgnoreCase)
            || string.Equals(p.Value.Value<string>(), tag, StringComparison.OrdinalIgnoreCase));
    }

    private string DataPath(string key) => $"v1/{_options.Mount}/data/{Uri.EscapeDataString(key)}";
    private string MetadataPath(string key) => $"v1/{_options.Mount}/metadata/{Uri.EscapeDataString(key)}";

    private static StringContent JsonContent(JObject payload)
        => new(payload.ToString(Formatting.None), Encoding.UTF8, "application/json");

    /// <summary>
    /// İsteği geçici hatalarda (5xx, 429, ağ hatası) exponential backoff ile
    /// yeniden dener. Her denemede yeni bir HttpRequestMessage üretilir.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<HttpRequestMessage> requestFactory)
    {
        var attempts = Math.Max(1, _options.MaxRetries);
        HttpResponseMessage? response = null;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            using var request = requestFactory();
            try
            {
                response = await _http.SendAsync(request);
            }
            catch (HttpRequestException ex) when (attempt < attempts)
            {
                _logger.LogWarning(ex,
                    "Vault isteği başarısız (deneme {Attempt}/{Max}), tekrar denenecek.",
                    attempt, attempts);
                await DelayAsync(attempt);
                continue;
            }

            if (IsTransient(response.StatusCode) && attempt < attempts)
            {
                _logger.LogWarning(
                    "Vault geçici hata {Status} (deneme {Attempt}/{Max}), tekrar denenecek.",
                    (int)response.StatusCode, attempt, attempts);
                response.Dispose();
                await DelayAsync(attempt);
                continue;
            }

            return response;
        }

        return response!;
    }

    private Task DelayAsync(int attempt)
        => Task.Delay(_options.RetryBaseDelayMs * (int)Math.Pow(2, attempt - 1));

    private static bool IsTransient(HttpStatusCode status)
        => (int)status >= 500 || status == HttpStatusCode.TooManyRequests;
}

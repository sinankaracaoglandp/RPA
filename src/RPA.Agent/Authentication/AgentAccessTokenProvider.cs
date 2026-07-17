namespace RPA.Agent.Authentication;

using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RPA.Agent.Configuration;

/// <summary>
/// Tum SignalR istemcilerinin paylastigi ajan erisim tokeni saglayicisi (Task 5).
/// </summary>
public interface IAgentAccessTokenProvider
{
    /// <summary>Gecerli erisim tokenini dondurur; gerekiyorsa yeniler.</summary>
    Task<string> GetTokenAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Erisim tokenini onbellege alir, son kullanmadan 2 dk once proaktif yeniler ve eszamanli
/// yenilemeleri bir semafor ile serilestirir (tasarim "Short-lived token").
///
/// GUVENLIK:
/// - Credential ve tam JWT hicbir zaman loglanmaz.
/// - Token omru yalnizca BASARILI API yaniti sonrasi cozulur; JWT claim'leri istemci tarafinda
///   yetkilendirme karari olarak KULLANILMAZ — yetki tek otorite olan WebAPI'de uygulanir.
///   Expiry cozumu sadece "ne zaman yenilemeliyim" zamanlamasi icindir.
/// </summary>
public sealed class AgentAccessTokenProvider : IAgentAccessTokenProvider, IDisposable
{
    /// <summary>Proaktif yenileme penceresi (tasarim: son kullanmadan 2 dk once).</summary>
    private static readonly TimeSpan RenewalWindow = TimeSpan.FromMinutes(2);

    private readonly IAgentTokenClient _client;
    private readonly IAgentCredentialStore _store;
    private readonly AgentOptions _options;
    private readonly ILogger<AgentAccessTokenProvider> _logger;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly TimeProvider _timeProvider;

    private string? _token;
    private DateTimeOffset _expiresAt;

    public AgentAccessTokenProvider(
        IAgentTokenClient client,
        IAgentCredentialStore store,
        IOptions<AgentOptions> options,
        ILogger<AgentAccessTokenProvider> logger,
        TimeProvider? timeProvider = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (TryGetCachedToken(out var cached))
        {
            return cached;
        }

        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            // Baska bir cagri kapiyi beklerken yenilemis olabilir — tek istek garantisi.
            if (TryGetCachedToken(out cached))
            {
                return cached;
            }

            var credential = _store.TryGetCredential()
                ?? throw new InvalidOperationException(
                    "Ajan credential'i bulunamadi — ajan aktive edilmemis olabilir.");

            var token = await _client.RequestAccessTokenAsync(_options.AgentId, credential, cancellationToken);

            _token = token;
            _expiresAt = ReadExpiry(token);
            _logger.LogInformation("Ajan erisim tokeni yenilendi; son kullanma {ExpiresAt:o}.", _expiresAt);
            return token;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Credential/JWT icermeyen ozet log; iceriden gelen mesaj yalnizca sunucu hata kodudur.
            _logger.LogWarning("Ajan erisim tokeni alinamadi: {Reason}", ex.Message);
            _token = null;
            _expiresAt = default;
            throw;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private bool TryGetCachedToken(out string token)
    {
        var cached = _token;
        if (cached is not null && _timeProvider.GetUtcNow() < _expiresAt - RenewalWindow)
        {
            token = cached;
            return true;
        }

        token = string.Empty;
        return false;
    }

    /// <summary>
    /// Token omrunu yalnizca yenileme zamanlamasi icin cozer. Cozulemezse token hemen
    /// yenilenmesi gereken kabul edilir (guvenli taraf).
    /// </summary>
    private DateTimeOffset ReadExpiry(string token)
    {
        try
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
            return new DateTimeOffset(jwt.ValidTo, TimeSpan.Zero);
        }
        catch (Exception ex) when (ex is ArgumentException or System.Text.Json.JsonException)
        {
            _logger.LogWarning("Ajan erisim tokeninin son kullanma bilgisi cozulemedi; her cagride yenilenecek.");
            return _timeProvider.GetUtcNow();
        }
    }

    public void Dispose() => _refreshGate.Dispose();
}

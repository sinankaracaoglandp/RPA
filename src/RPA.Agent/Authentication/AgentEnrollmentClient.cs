namespace RPA.Agent.Authentication;

using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using RPA.Agent.Configuration;

/// <summary>
/// Ajan token istegi sozlesmesi — <see cref="AgentAccessTokenProvider"/> tarafindan kullanilir.
/// Ayri arayuz olmasi saglayicinin HTTP olmadan test edilebilmesini saglar.
/// </summary>
public interface IAgentTokenClient
{
    /// <summary>Credential'i kisa omurlu erisim JWT'si ile takas eder. Basarisizlikta firlatir.</summary>
    Task<string> RequestAccessTokenAsync(Guid agentId, string credential, CancellationToken cancellationToken);
}

/// <summary>
/// WebAPI <c>/api/agent-auth</c> uc noktalarinin istemcisi (tasarim "Agent Enrollment and Authentication").
/// Aktivasyon kodu tek kullanimliktir; donen credential yalnizca <see cref="IAgentCredentialStore"/>'a yazilir.
/// GUVENLIK: hata mesajlari sunucunun stabil hata kodunu tasir; credential/JWT hicbir yolda loglanmaz.
/// </summary>
public sealed class AgentEnrollmentClient : IAgentTokenClient
{
    private readonly HttpClient _http;
    private readonly AgentOptions _options;
    private readonly IAgentCredentialStore _store;

    public AgentEnrollmentClient(HttpClient http, IOptions<AgentOptions> options, IAgentCredentialStore store)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>
    /// Aktivasyon kodunu kullanarak ajani baglar ve donen credential'i korumali depoya yazar.
    /// AgentId/InstallationId yapilandirmadan (<see cref="AgentOptions"/>) alinir — CLI
    /// (<c>--activate</c>) yolu bunu kullanir.
    /// </summary>
    public Task ActivateAsync(string activationCode, CancellationToken cancellationToken = default)
        => ActivateAsync(_options.AgentId, _options.InstallationId, activationCode, cancellationToken);

    /// <summary>
    /// AgentId ve InstallationId'nin ACIKCA verildigi aktivasyon — operatorun bu degerleri
    /// appsettings.json'a yazmadan ekrandan girebilmesi icin (aktivasyon penceresi kullanir).
    /// Diger her sey (adres, makine parmak izi, credential depolama) yapilandirmadan gelir.
    /// </summary>
    public async Task ActivateAsync(Guid agentId, string installationId, string activationCode, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activationCode);
        if (agentId == Guid.Empty)
        {
            throw new ArgumentException("Agent kimligi (AgentId) bos olamaz.", nameof(agentId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(installationId);

        using var response = await _http.PostAsJsonAsync(
            BuildUri("/api/agent-auth/activate"),
            new
            {
                agentId,
                installationId,
                activationCode,
                machineFingerprint = _options.EffectiveMachineName,
            },
            cancellationToken);

        await EnsureSuccessAsync(response, "Ajan aktivasyonu basarisiz", cancellationToken);

        var body = await response.Content.ReadFromJsonAsync<ActivateResponseBody>(cancellationToken)
            ?? throw new InvalidOperationException("Ajan aktivasyonu basarisiz: bos yanit.");
        if (string.IsNullOrWhiteSpace(body.Credential))
        {
            throw new InvalidOperationException("Ajan aktivasyonu basarisiz: credential donmedi.");
        }

        _store.SaveCredential(body.Credential);
    }

    public async Task<string> RequestAccessTokenAsync(Guid agentId, string credential, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync(
            BuildUri("/api/agent-auth/token"),
            new { agentId, credential },
            cancellationToken);

        await EnsureSuccessAsync(response, "Ajan token istegi basarisiz", cancellationToken);

        var body = await response.Content.ReadFromJsonAsync<TokenResponseBody>(cancellationToken);
        if (body is null || string.IsNullOrWhiteSpace(body.AccessToken))
        {
            throw new InvalidOperationException("Ajan token istegi basarisiz: bos yanit.");
        }

        return body.AccessToken;
    }

    private Uri BuildUri(string path) => new($"{_options.OrchestratorUrl.TrimEnd('/')}{path}");

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string prefix, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        // Yalnizca sunucunun stabil hata kodu tasinir — istek govdesi (credential) asla.
        string? code = null;
        try
        {
            code = (await response.Content.ReadFromJsonAsync<ErrorBody>(ct))?.Error;
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            // Hata govdesi JSON degil — yalnizca durum kodu raporlanir.
        }

        throw new InvalidOperationException($"{prefix}: {(int)response.StatusCode} {code ?? response.StatusCode.ToString()}");
    }

    private sealed record ActivateResponseBody(Guid AgentId, string Credential);
    private sealed record TokenResponseBody(string AccessToken);
    private sealed record ErrorBody(string? Error);
}

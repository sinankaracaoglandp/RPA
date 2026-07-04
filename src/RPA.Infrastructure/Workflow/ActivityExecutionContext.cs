namespace RPA.Infrastructure.Workflow;

using Microsoft.Extensions.Logging;
using RPA.Domain.Interfaces;
using DomainLogLevel = RPA.Domain.Interfaces.LogLevel;

/// <summary>
/// <see cref="IActivityExecutionContext"/> somut implementasyonu.
/// Değişken scope zinciri, credential vault, asset erişimi ve korelasyonlu loglama sağlar.
/// BaseRunner her çalıştırma için bir örnek oluşturur (scope paylaşımlı).
/// Spec Bölüm 5.2, 5.5, 11.
/// </summary>
public sealed class ActivityExecutionContext : IActivityExecutionContext
{
    private readonly VariableScope _scope;
    private readonly ICredentialVault? _vault;
    private readonly IReadOnlyDictionary<string, string?>? _assets;
    private readonly ILogger _logger;

    public ActivityExecutionContext(
        VariableScope scope,
        Guid jobRunId,
        ILogger logger,
        ICredentialVault? vault = null,
        IReadOnlyDictionary<string, string?>? assets = null,
        string timeZone = "UTC")
    {
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        JobRunId = jobRunId;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _vault = vault;
        _assets = assets;
        TimeZone = timeZone;
    }

    public string TimeZone { get; }

    public Guid JobRunId { get; }

    public T GetVariable<T>(string name) => _scope.GetVariable<T>(name);

    public void SetVariable(string name, object? value) => _scope.SetVariable(name, value);

    public async Task<string> GetCredentialAsync(string credentialName)
    {
        if (_vault is null)
        {
            throw new InvalidOperationException(
                $"Credential vault yapılandırılmamış — '{credentialName}' alınamıyor.");
        }
        var secret = await _vault.GetSecretAsync(credentialName);
        // Not: Decrypt yalnızca aktivite çalıştırma anında; değer asla loglanmaz.
        return secret.Decrypt();
    }

    public Task<string?> GetAssetAsync(string assetName)
    {
        if (_assets is not null && _assets.TryGetValue(assetName, out var value))
        {
            return Task.FromResult(value);
        }
        return Task.FromResult<string?>(null);
    }

    public void Log(string message, DomainLogLevel level = DomainLogLevel.Information)
    {
        // Korelasyon ID (JobRunId) her kayda eklenir — Elasticsearch sorgusu için.
        _logger.Log(MapLevel(level), "{Message} (JobRun {JobRunId})", message, JobRunId);
    }

    private static Microsoft.Extensions.Logging.LogLevel MapLevel(DomainLogLevel level) => level switch
    {
        DomainLogLevel.Debug => Microsoft.Extensions.Logging.LogLevel.Debug,
        DomainLogLevel.Information => Microsoft.Extensions.Logging.LogLevel.Information,
        DomainLogLevel.Warning => Microsoft.Extensions.Logging.LogLevel.Warning,
        DomainLogLevel.Error => Microsoft.Extensions.Logging.LogLevel.Error,
        DomainLogLevel.Critical => Microsoft.Extensions.Logging.LogLevel.Critical,
        _ => Microsoft.Extensions.Logging.LogLevel.Information,
    };
}

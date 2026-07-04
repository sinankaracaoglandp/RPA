namespace RPA.Infrastructure.Workflow;

using Microsoft.Extensions.Logging;
using RPA.Domain.Interfaces;
using SystemException = RPA.Domain.Exceptions.SystemException;

/// <summary>
/// Component çağrısı için ayrılmış çalıştırıcı (Task 2.4.1). Sorumlulukları:
/// versiyon pinleme (<see cref="IComponentResolver"/>), kullanım takibi
/// (<see cref="IComponentUsageTracker"/>) ve component'i izole scope'ta yürütmek
/// (<see cref="IWorkflowRunner.InvokeComponentAsync"/>). BaseRunner'ın componentCall
/// node'ları da aynı yolu izler; bu sınıf ayrıca doğrudan (programatik) çağrı sağlar.
/// Spec Bölüm 5.4.
/// </summary>
public sealed class ComponentRunner
{
    private readonly IComponentResolver _resolver;
    private readonly IWorkflowRunner _runner;
    private readonly IComponentUsageTracker? _usageTracker;
    private readonly ILogger<ComponentRunner>? _logger;

    public ComponentRunner(
        IComponentResolver resolver,
        IWorkflowRunner runner,
        IComponentUsageTracker? usageTracker = null,
        ILogger<ComponentRunner>? logger = null)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _usageTracker = usageTracker;
        _logger = logger;
    }

    /// <summary>
    /// Bir component'i çözer, kullanımını kaydeder ve izole scope'ta çalıştırır.
    /// </summary>
    /// <param name="componentId">Component tanımlayıcısı.</param>
    /// <param name="version">Pinlenmiş SemVer; null ise en son Published.</param>
    /// <param name="inputs">Component giriş parametreleri.</param>
    /// <param name="jobRunId">Korelasyon ID.</param>
    /// <param name="workflowVersionId">Çağıran workflow versiyonu (kullanım takibi için, opsiyonel).</param>
    /// <param name="cancellationToken">İptal sinyali.</param>
    /// <returns>Component çıkış değerleri.</returns>
    public async Task<Dictionary<string, object?>> InvokeAsync(
        string componentId,
        string? version,
        Dictionary<string, object?> inputs,
        Guid jobRunId,
        Guid workflowVersionId = default,
        CancellationToken cancellationToken = default)
    {
        var componentVersion = _resolver.Resolve(componentId, version)
            ?? throw new SystemException(
                $"Component çözülemedi: '{componentId}' v{version ?? "(latest)"} — " +
                "yayımlanmış versiyon bulunamadı.");

        _logger?.LogInformation(
            "Component {ComponentId} çözüldü → v{Version} (pin: {Pin})",
            componentId, componentVersion.Version, version ?? "latest");

        if (workflowVersionId != Guid.Empty)
        {
            _usageTracker?.Record(workflowVersionId, componentVersion);
        }

        return await _runner.InvokeComponentAsync(
            componentVersion, inputs, jobRunId, cancellationToken);
    }
}

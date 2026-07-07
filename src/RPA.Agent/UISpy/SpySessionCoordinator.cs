namespace RPA.Agent.UISpy;

using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RPA.Domain.ValueObjects;
using RPA.Infrastructure.UISpy;

public interface ISapGuiSinglePicker
{
    Task<SapGuiElement?> DetectOnceAsync(CancellationToken cancellationToken = default);
}

public sealed class SpySessionOptions
{
    public const string SectionName = "SpySession";
    public int TimeoutSeconds { get; set; } = 60;
}

public interface ISpySessionCoordinator
{
    Task StartAsync(Guid sessionId, string kind, CancellationToken cancellationToken = default);
    Task StopAsync(Guid sessionId, CancellationToken cancellationToken = default);
}

public sealed class SpySessionCoordinator : ISpySessionCoordinator
{
    private readonly ISapGuiSinglePicker _sapPicker;
    private readonly ISpyElementTransport _transport;
    private readonly SpySessionOptions _options;
    private readonly ILogger<SpySessionCoordinator> _logger;
    private readonly object _gate = new();
    private Guid _activeSessionId;
    private CancellationTokenSource? _activeCts;

    public SpySessionCoordinator(
        ISapGuiSinglePicker sapPicker,
        ISpyElementTransport transport,
        IOptions<SpySessionOptions> options,
        ILogger<SpySessionCoordinator> logger)
    {
        _sapPicker = sapPicker ?? throw new ArgumentNullException(nameof(sapPicker));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(Guid sessionId, string kind, CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty)
        {
            throw new InvalidOperationException("SessionId zorunludur.");
        }

        if (!string.Equals(kind, "sap", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Desteklenmeyen spy tipi: {kind}");
        }

        CancellationTokenSource linkedCts;
        lock (_gate)
        {
            if (_activeCts is not null)
            {
                throw new InvalidOperationException("Aktif UI Spy oturumu zaten var.");
            }

            _activeSessionId = sessionId;
            _activeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts = _activeCts;
        }

        try
        {
            var timeout = TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds));
            linkedCts.CancelAfter(timeout);
            var element = await _sapPicker.DetectOnceAsync(linkedCts.Token);
            if (element is null)
            {
                return;
            }

            await _transport.SendAsync(SpyElementMessage.From(element, sessionId), linkedCts.Token);
            _logger.LogInformation("UI Spy: session {SessionId} icin SAP element secildi {ElementId}.", sessionId, element.Id);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("UI Spy: session {SessionId} iptal edildi veya zaman asimina ugradi.", sessionId);
        }
        finally
        {
            ClearSession(sessionId, linkedCts);
        }
    }

    public Task StopAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? cts = null;
        lock (_gate)
        {
            if (_activeCts is null)
            {
                return Task.CompletedTask;
            }

            if (sessionId != Guid.Empty && sessionId != _activeSessionId)
            {
                return Task.CompletedTask;
            }

            cts = _activeCts;
        }

        cts.Cancel();
        return Task.CompletedTask;
    }

    private void ClearSession(Guid sessionId, CancellationTokenSource cts)
    {
        lock (_gate)
        {
            if (_activeSessionId == sessionId && ReferenceEquals(_activeCts, cts))
            {
                _activeSessionId = Guid.Empty;
                _activeCts = null;
            }
        }

        cts.Dispose();
    }
}

[SupportedOSPlatform("windows")]
public sealed class SapGuiSinglePicker : ISapGuiSinglePicker
{
    private readonly SapGuiElementDetector _detector;

    public SapGuiSinglePicker(SapGuiElementDetector detector)
    {
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
    }

    public Task<SapGuiElement?> DetectOnceAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_detector.DetectElementUnderCursor());
    }
}

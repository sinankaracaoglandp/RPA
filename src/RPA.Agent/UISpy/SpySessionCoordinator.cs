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

/// <summary>Masaüstü (UIA/FlaUI) tek-seçim picker'ı — imleç altındaki elementi onayla.</summary>
public interface IDesktopSinglePicker
{
    Task<DesktopUiElement?> DetectOnceAsync(CancellationToken cancellationToken = default);
}

/// <summary>Web (Playwright/DOM) tek-seçim picker'ı — tarayıcıda CTRL+Tık ile elementi seç.</summary>
public interface IWebSinglePicker
{
    Task<WebUiElement?> DetectOnceAsync(CancellationToken cancellationToken = default);
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
    private readonly IDesktopSinglePicker? _desktopPicker;
    private readonly IWebSinglePicker? _webPicker;
    private readonly object _gate = new();
    private Guid _activeSessionId;
    private CancellationTokenSource? _activeCts;

    public SpySessionCoordinator(
        ISapGuiSinglePicker sapPicker,
        ISpyElementTransport transport,
        IOptions<SpySessionOptions> options,
        ILogger<SpySessionCoordinator> logger,
        IDesktopSinglePicker? desktopPicker = null,
        IWebSinglePicker? webPicker = null)
    {
        _sapPicker = sapPicker ?? throw new ArgumentNullException(nameof(sapPicker));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _desktopPicker = desktopPicker;
        _webPicker = webPicker;
    }

    public async Task StartAsync(Guid sessionId, string kind, CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty)
        {
            throw new InvalidOperationException("SessionId zorunludur.");
        }

        var isSap = string.Equals(kind, "sap", StringComparison.OrdinalIgnoreCase);
        var isDesktop = string.Equals(kind, "desktop", StringComparison.OrdinalIgnoreCase);
        var isWeb = string.Equals(kind, "web", StringComparison.OrdinalIgnoreCase);
        if (!isSap && !isDesktop && !isWeb)
        {
            throw new InvalidOperationException($"Desteklenmeyen spy tipi: {kind}");
        }

        if (isDesktop && _desktopPicker is null)
        {
            throw new InvalidOperationException("Masaüstü picker bu ortamda kayıtlı değil (yalnız Windows).");
        }

        if (isWeb && _webPicker is null)
        {
            throw new InvalidOperationException("Web picker bu ortamda kayıtlı değil.");
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

            SpyElementMessage? message;
            if (isDesktop)
            {
                var element = await _desktopPicker!.DetectOnceAsync(linkedCts.Token);
                message = element is null ? null : SpyElementMessage.FromDesktop(element, sessionId);
            }
            else if (isWeb)
            {
                var element = await _webPicker!.DetectOnceAsync(linkedCts.Token);
                message = element is null ? null : SpyElementMessage.FromWeb(element, sessionId);
            }
            else
            {
                var element = await _sapPicker.DetectOnceAsync(linkedCts.Token);
                message = element is null ? null : SpyElementMessage.From(element, sessionId);
            }

            if (message is null)
            {
                _logger.LogDebug("UI Spy: session {SessionId} ({Kind}) secim yapilmadan iptal edildi.", sessionId, kind);
                await NotifyCancelledSafeAsync(sessionId);
                return;
            }

            await _transport.SendAsync(message, linkedCts.Token);
            _logger.LogInformation("UI Spy: session {SessionId} ({Kind}) icin element secildi {ElementId}.", sessionId, kind, message.ElementId);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("UI Spy: session {SessionId} iptal edildi veya zaman asimina ugradi.", sessionId);
            await NotifyCancelledSafeAsync(sessionId);
        }
        finally
        {
            ClearSession(sessionId, linkedCts);
        }
    }

    private async Task NotifyCancelledSafeAsync(Guid sessionId)
    {
        try
        {
            // Oturum token'i iptal olmus olabilir; bildirim best-effort ve token'sizdir.
            await _transport.NotifyCancelledAsync(sessionId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "UI Spy: iptal bildirimi gonderilemedi {SessionId}.", sessionId);
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

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

/// <summary>🎯 image bölge picker'ı — ekranda dikdörtgen çiz, PNG/koordinat döndür.</summary>
public interface IImageRegionPicker
{
    Task<ImagePick?> DetectOnceAsync(ImagePickerOptions options, CancellationToken cancellationToken = default);
}

/// <summary>Image picker sonucu: base64 PNG (image alanı için) ve/veya {x,y,width,height} JSON (region alanı için).</summary>
public sealed record ImagePick(string? ImageBase64, string? RegionJson);

/// <summary>🎯 text-offset picker'ı — çapa metnini seç + hedef noktaya tıkla, dx/dy hesapla.</summary>
public interface ITextOffsetPicker
{
    Task<TextOffsetPick?> DetectOnceAsync(ImagePickerOptions options, CancellationToken cancellationToken = default);
}

/// <summary>text-offset picker sonucu: çapa metni, dx/dy ofset ve çapa önizleme (base64 PNG).</summary>
public sealed record TextOffsetPick(string AnchorText, int Dx, int Dy, string? PreviewBase64);

/// <summary>
/// Image picker'ın "ekran dondurma" (freeze) davranışı. Geçici menü/pencere yakalamak için:
/// önce hedef UI açılır, sonra ekran dondurulup donmuş görüntü üzerinde seçim yapılır.
/// <para><see cref="CaptureMode"/>: <c>"f2"</c> = kullanıcı kısayola basınca dondur (süre sınırsız);
/// <c>"timer"</c> = <see cref="DelaySeconds"/> saniye geri sayıp otomatik dondur.</para>
/// <para>Manuel modda dondurma kısayolu node'dan gelir: <see cref="HotKey"/> (F1–F12) +
/// opsiyonel <see cref="Ctrl"/>/<see cref="Shift"/>/<see cref="Alt"/> — hedef uygulamada boş bir
/// tuş seçilebilsin diye.</para>
/// </summary>
public sealed record ImagePickerOptions(
    string CaptureMode, int DelaySeconds, string HotKey, bool Ctrl, bool Shift, bool Alt)
{
    public const string ModeF2 = "f2";
    public const string ModeTimer = "timer";
    public const string DefaultHotKey = "F2";

    public static ImagePickerOptions Default { get; } = new(ModeF2, 5, DefaultHotKey, false, false, false);

    /// <summary>Studio'dan gelen JSON ({captureMode, delaySeconds, hotKey, ctrl, shift, alt}); null/bozuk ise varsayılan.</summary>
    public static ImagePickerOptions Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Default;
        }
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            var mode = root.TryGetProperty("captureMode", out var m) ? m.GetString() : null;
            mode = string.Equals(mode, ModeTimer, StringComparison.OrdinalIgnoreCase) ? ModeTimer : ModeF2;
            var seconds = root.TryGetProperty("delaySeconds", out var s) && s.TryGetInt32(out var v) ? v : 5;
            seconds = Math.Clamp(seconds, 1, 120);
            var hotKey = NormalizeHotKey(root.TryGetProperty("hotKey", out var hk) ? hk.GetString() : null);
            return new ImagePickerOptions(mode, seconds, hotKey, Flag(root, "ctrl"), Flag(root, "shift"), Flag(root, "alt"));
        }
        catch (System.Text.Json.JsonException)
        {
            return Default;
        }
    }

    private static bool Flag(System.Text.Json.JsonElement root, string name)
        => root.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.True;

    /// <summary>F1–F12 dışını (veya null) varsayılana (F2) indirger.</summary>
    private static string NormalizeHotKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return DefaultHotKey;
        }
        key = key.Trim().ToUpperInvariant();
        if (key.Length >= 2 && key[0] == 'F' && int.TryParse(key.AsSpan(1), out var n) && n is >= 1 and <= 12)
        {
            return $"F{n}";
        }
        return DefaultHotKey;
    }

    /// <summary>Windows sanal-tuş kodu (F1=0x70 … F12=0x7B).</summary>
    public uint VirtualKey => 0x70u + (uint)(FunctionKeyNumber - 1);

    private int FunctionKeyNumber => int.TryParse(HotKey.AsSpan(1), out var n) && n is >= 1 and <= 12 ? n : 2;

    /// <summary>RegisterHotKey fsModifiers: MOD_ALT=1, MOD_CONTROL=2, MOD_SHIFT=4.</summary>
    public uint Modifiers => (Ctrl ? 2u : 0u) | (Shift ? 4u : 0u) | (Alt ? 1u : 0u);

    /// <summary>Kullanıcıya gösterilecek kombinasyon (örn. "Ctrl+Shift+F3").</summary>
    public string DisplayCombo
    {
        get
        {
            var prefix =
                (Ctrl ? "Ctrl+" : string.Empty) +
                (Shift ? "Shift+" : string.Empty) +
                (Alt ? "Alt+" : string.Empty);
            return prefix + HotKey;
        }
    }
}

public sealed class SpySessionOptions
{
    public const string SectionName = "SpySession";
    public int TimeoutSeconds { get; set; } = 60;
}

public interface ISpySessionCoordinator
{
    Task StartAsync(Guid sessionId, string kind, string? optionsJson = null, CancellationToken cancellationToken = default);
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
    private readonly IImageRegionPicker? _imagePicker;
    private readonly ITextOffsetPicker? _textOffsetPicker;
    private readonly object _gate = new();
    private Guid _activeSessionId;
    private CancellationTokenSource? _activeCts;

    public SpySessionCoordinator(
        ISapGuiSinglePicker sapPicker,
        ISpyElementTransport transport,
        IOptions<SpySessionOptions> options,
        ILogger<SpySessionCoordinator> logger,
        IDesktopSinglePicker? desktopPicker = null,
        IWebSinglePicker? webPicker = null,
        IImageRegionPicker? imagePicker = null,
        ITextOffsetPicker? textOffsetPicker = null)
    {
        _sapPicker = sapPicker ?? throw new ArgumentNullException(nameof(sapPicker));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _desktopPicker = desktopPicker;
        _webPicker = webPicker;
        _imagePicker = imagePicker;
        _textOffsetPicker = textOffsetPicker;
    }

    public async Task StartAsync(Guid sessionId, string kind, string? optionsJson = null, CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty)
        {
            throw new InvalidOperationException("SessionId zorunludur.");
        }

        var isSap = string.Equals(kind, "sap", StringComparison.OrdinalIgnoreCase);
        var isDesktop = string.Equals(kind, "desktop", StringComparison.OrdinalIgnoreCase);
        var isWeb = string.Equals(kind, "web", StringComparison.OrdinalIgnoreCase);
        var isImage = string.Equals(kind, "image", StringComparison.OrdinalIgnoreCase);
        var isTextOffset = string.Equals(kind, "text-offset", StringComparison.OrdinalIgnoreCase);
        if (!isSap && !isDesktop && !isWeb && !isImage && !isTextOffset)
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

        if (isImage && _imagePicker is null)
        {
            throw new InvalidOperationException("Image picker bu ortamda kayıtlı değil (yalnız Windows).");
        }

        if (isTextOffset && _textOffsetPicker is null)
        {
            throw new InvalidOperationException("Metin-ofset picker bu ortamda kayıtlı değil (yalnız Windows).");
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
            // Image picker'da kullanıcı hedef menüyü/pencereyi elle açtığı için (F2/zamanlayıcı ile
            // dondurma) daha uzun süre gerekir; diğer picker'lar için normal timeout uygulanır.
            var timeoutSeconds = Math.Max(1, _options.TimeoutSeconds);
            if (isImage || isTextOffset)
            {
                timeoutSeconds = Math.Max(timeoutSeconds, 300);
            }
            linkedCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

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
            else if (isImage)
            {
                var pickerOptions = ImagePickerOptions.Parse(optionsJson);
                var pick = await _imagePicker!.DetectOnceAsync(pickerOptions, linkedCts.Token);
                message = pick is null ? null : SpyElementMessage.FromImage(pick.ImageBase64, pick.RegionJson, sessionId);
            }
            else if (isTextOffset)
            {
                var pickerOptions = ImagePickerOptions.Parse(optionsJson);
                var pick = await _textOffsetPicker!.DetectOnceAsync(pickerOptions, linkedCts.Token);
                message = pick is null ? null : SpyElementMessage.FromTextOffset(pick.AnchorText, pick.Dx, pick.Dy, pick.PreviewBase64, sessionId);
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

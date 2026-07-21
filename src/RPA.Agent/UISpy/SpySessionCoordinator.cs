namespace RPA.Agent.UISpy;

using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RPA.Domain.ValueObjects;
using RPA.Infrastructure.UISpy;

/// <summary>
/// SAP GUI tek-seçim picker'ı. Onay, kullanıcının seçtiği tuş kombinasyonuyla verilir
/// (<see cref="ImagePickerOptions.HotKey"/> + Ctrl/Shift/Alt) — SAP ekranında fare tıklaması
/// alanı/butonu tetikleyeceği için seçim tıklamayla onaylanmaz.
/// </summary>
public interface ISapGuiSinglePicker
{
    Task<SapGuiElement?> DetectOnceAsync(ImagePickerOptions options, CancellationToken cancellationToken = default);
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

/// <summary>🎯 klasör picker'ı — agent makinesinde native klasör seçim diyaloğu açar, seçilen yolu döndürür (iptal → null).</summary>
public interface IFolderPicker
{
    Task<string?> DetectOnceAsync(CancellationToken cancellationToken = default);
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

    /// <summary>
    /// Caps Lock — SAP için önerilen onay tuşu: SAP'ta F1–F12'nin tamamı transaction kısayoludur,
    /// Caps Lock ise hiçbir SAP fonksiyonunu tetiklemez.
    /// </summary>
    public const string CapsLockKey = "CapsLock";

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

    /// <summary>
    /// F1–F12, CapsLock ve tek harf (A–Z) dışını (veya null) varsayılana (F2) indirger.
    /// <para>Harf tuşları Ctrl/Shift/Alt ile birlikte kullanılmak içindir (örn. Ctrl+T): SAP'ta
    /// F1–F12 doludur, harf kombinasyonları ise serbesttir.</para>
    /// </summary>
    private static string NormalizeHotKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return DefaultHotKey;
        }
        key = key.Trim().ToUpperInvariant();
        if (key is "CAPSLOCK" or "CAPS_LOCK" or "CAPS")
        {
            return CapsLockKey;
        }
        if (key.Length >= 2 && key[0] == 'F' && int.TryParse(key.AsSpan(1), out var n) && n is >= 1 and <= 12)
        {
            return $"F{n}";
        }
        if (key.Length == 1 && key[0] is >= 'A' and <= 'Z')
        {
            return key;
        }
        return DefaultHotKey;
    }

    /// <summary>
    /// Windows sanal-tuş kodu (CapsLock=0x14; A–Z=0x41–0x5A; F1=0x70 … F12=0x7B).
    /// </summary>
    public uint VirtualKey
    {
        get
        {
            if (IsCapsLock)
            {
                return 0x14u;
            }

            // Harf tuşlarının sanal-tuş kodu büyük harfin ASCII değeridir.
            if (IsLetter)
            {
                return HotKey[0];
            }

            return 0x70u + (uint)(FunctionKeyNumber - 1);
        }
    }

    private bool IsCapsLock => string.Equals(HotKey, CapsLockKey, StringComparison.OrdinalIgnoreCase);

    private bool IsLetter => HotKey.Length == 1 && HotKey[0] is >= 'A' and <= 'Z';

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
    private readonly IFolderPicker? _folderPicker;
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
        ITextOffsetPicker? textOffsetPicker = null,
        IFolderPicker? folderPicker = null)
    {
        _sapPicker = sapPicker ?? throw new ArgumentNullException(nameof(sapPicker));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _desktopPicker = desktopPicker;
        _webPicker = webPicker;
        _imagePicker = imagePicker;
        _textOffsetPicker = textOffsetPicker;
        _folderPicker = folderPicker;
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
        var isFolder = string.Equals(kind, "folder", StringComparison.OrdinalIgnoreCase);
        if (!isSap && !isDesktop && !isWeb && !isImage && !isTextOffset && !isFolder)
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

        if (isFolder && _folderPicker is null)
        {
            throw new InvalidOperationException("Klasör picker bu ortamda kayıtlı değil (yalnız Windows).");
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
            // SAP seçiminde de kullanıcı hedef ekrana elle gider (transaction açar, sayfa gezer);
            // 60 sn'lik varsayılan yetmez.
            if (isImage || isTextOffset || isFolder || isSap)
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
            else if (isFolder)
            {
                var folderPath = await _folderPicker!.DetectOnceAsync(linkedCts.Token);
                message = string.IsNullOrWhiteSpace(folderPath) ? null : SpyElementMessage.FromFolder(folderPath, sessionId);
            }
            else
            {
                    var pickerOptions = ImagePickerOptions.Parse(optionsJson);
                var element = await _sapPicker.DetectOnceAsync(pickerOptions, linkedCts.Token);
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

/// <summary>
/// SAP GUI tek-seçim picker'ı — masaüstü picker'ıyla (<c>FlaUiDesktopSinglePicker</c>) birebir aynı
/// kullanıcı deneyimi: tasarımcı penceresi küçültülür, imleç SAP alanları üzerinde gezinirken element
/// vurgulanır, sol tıklama seçimi onaylar, <c>Esc</c> iptal eder.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SapGuiSinglePicker : ISapGuiSinglePicker
{
    private const int VkLButton = 0x01;
    private const int VkEscape = 0x1B;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12; // Alt

    private readonly SapGuiElementDetector _detector;
    private readonly IPickerWindowManager _windows;
    private readonly ILogger<SapGuiSinglePicker> _logger;

    public SapGuiSinglePicker(
        SapGuiElementDetector detector,
        IPickerWindowManager windows,
        ILogger<SapGuiSinglePicker> logger)
    {
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
        _windows = windows ?? throw new ArgumentNullException(nameof(windows));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SapGuiElement?> DetectOnceAsync(
        ImagePickerOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        // AKIŞ (her iki modda da aynı): hazırlık → seçim turu → sol tıkla seç.
        //
        // Hazırlık aşaması kullanıcının hedef SAP ekranını açması içindir; seçim turu ancak
        // bittikten sonra başlar. Bitiş sinyali moda göre değişir:
        //   "timer" → geri sayım dolar,
        //   "f2"    → kullanıcı seçtiği tuş kombinasyonuna basar (varsayılan Ctrl+T).
        //
        // Seçim SOL TIKLAMA ile alınır. SAP'ta F1–F12 transaction kısayolu olduğundan tuşu
        // "onay" olarak kullanmak güvenilir değildi; tuş yalnızca süreci BAŞLATIR.
        var useTimer = string.Equals(
            options.CaptureMode, ImagePickerOptions.ModeTimer, StringComparison.OrdinalIgnoreCase);

        // Tek ekran kullanımı: öndeki pencere (tasarımcı tarayıcısı) SAP penceresini kapatır.
        var restore = _windows.MinimizeForeground();

        try
        {
            if (useTimer)
            {
                _logger.LogInformation(
                    "SAP UI Spy: {Seconds} sn hazırlık — hedef SAP ekranını açın; süre bitince seçim başlayacak.",
                    options.DelaySeconds);

                if (!await WaitForCountdownAsync(options.DelaySeconds, cancellationToken))
                {
                    _logger.LogDebug("SAP UI Spy: geri sayım sırasında Esc ile iptal edildi.");
                    return null;
                }
            }
            else
            {
                _logger.LogInformation(
                    "SAP UI Spy: hazırlık — hedef SAP ekranını açın, sonra {Combo} tuşuna basın; Esc iptal.",
                    options.DisplayCombo);

                if (!await WaitForHotKeyAsync(options, cancellationToken))
                {
                    _logger.LogDebug("SAP UI Spy: tuş beklenirken Esc ile iptal edildi.");
                    return null;
                }
            }

            _logger.LogInformation(
                "SAP UI Spy: seçim başladı — hedef alanın üzerine gelin (kırmızı çerçeve), sol tıklayın; Esc iptal.");

            // Hazırlık sırasındaki tıklamalar (🎯 düğmesi, SAP'ta gezinme) seçim sanılmasın:
            // sol buton serbest bırakılmış halde başlanmalı.
            while (IsKeyDown(VkLButton))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(20, cancellationToken);
            }
            GetAsyncKeyState(VkLButton);

            return await RunSelectionLoopAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            // Seçim bitti/iptal edildi: ekranda vurgu çerçevesi bırakma.
            _detector.ClearHighlight();
            restore();
        }
    }

    /// <summary>
    /// Hazırlık geri sayımı. Kullanıcı bu sürede hedef SAP ekranına gider. <c>false</c> = Esc ile
    /// iptal edildi.
    /// </summary>
    private async Task<bool> WaitForCountdownAsync(int seconds, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(Math.Max(1, seconds));
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsKeyDown(VkEscape))
            {
                return false;
            }

            await Task.Delay(50, cancellationToken);
        }

        return true;
    }

    /// <summary>
    /// Kullanıcının seçim sürecini başlatan tuş kombinasyonuna basmasını bekler (süre sınırsız —
    /// oturum zaman aşımına kadar). <c>false</c> = Esc ile iptal edildi.
    /// </summary>
    private async Task<bool> WaitForHotKeyAsync(ImagePickerOptions options, CancellationToken cancellationToken)
    {
        // Tuş serbest bırakılmış halde başlamalı (🎯'e basarken sızan tuş anında tetiklemesin).
        while (IsKeyDown((int)options.VirtualKey))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(20, cancellationToken);
        }
        GetAsyncKeyState((int)options.VirtualKey); // birikmiş "basıldı" bitini temizle

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsKeyDown(VkEscape))
            {
                return false;
            }

            // 0x8000 = şu an basılı, 0x0001 = son okumadan bu yana basıldı (kısa basış
            // yoklama araları arasına düşüp kaçırılmasın). Modifier'lar basış ANINDA okunur.
            var keyState = GetAsyncKeyState((int)options.VirtualKey);
            if (((keyState & 0x8000) != 0 || (keyState & 0x0001) != 0) && ModifiersHeld(options))
            {
                return true;
            }

            await Task.Delay(30, cancellationToken);
        }
    }

    /// <summary>
    /// Seçim turu: imleç altındaki element kırmızı çerçeveyle vurgulanır, seçim SOL TIKLAMA ile
    /// alınır. <c>null</c> = Esc ile iptal.
    /// </summary>
    private async Task<SapGuiElement?> RunSelectionLoopAsync(CancellationToken cancellationToken)
    {
        string? lastHighlightedId = null;
        var nextDiagnosticAt = DateTime.UtcNow.AddSeconds(2);
        var samples = 0;
        var resolved = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsKeyDown(VkEscape))
            {
                // Özet, "hiç mi çözülemedi yoksa onay mı alınamadı" ayrımını netleştirir.
                _logger.LogInformation(
                    "SAP UI Spy: Esc ile iptal edildi — {Samples} örnek alındı, {Resolved} tanesinde SAP elementi çözüldü.",
                    samples, resolved);
                return null;
            }

            var (x, y) = CursorPosition();
            var element = _detector.DetectElementAt(x, y);
            samples++;
            if (element is not null)
            {
                resolved++;
            }

            if (element is not null && element.Id != lastHighlightedId)
            {
                // Kullanıcı neyin seçileceğini görsün (SAP'ın kendi kırmızı çerçevesi).
                _detector.HighlightAt(x, y);
                lastHighlightedId = element.Id;
            }
            else if (element is null && DateTime.UtcNow >= nextDiagnosticAt)
            {
                // Sessiz başarısızlık olmasın: element çözülemiyorsa SEBEBİNİ düzenli olarak
                // görünür seviyede logla (kullanıcı "hiçbir şey olmuyor" ile baş başa kalmasın).
                nextDiagnosticAt = DateTime.UtcNow.AddSeconds(2);
                _logger.LogWarning("SAP UI Spy: element bulunamadı — {Reason}", _detector.Diagnose(x, y));
            }

            // Onay durumunu tur başına TEK kez oku: 0x8000 = şu an basılı, 0x0001 = son okumadan
            // bu yana basıldı (hızlı tık/basış aksi halde 40 ms yoklama araları arasına düşüp
            // kaçırılır).
            var button = GetAsyncKeyState(VkLButton);
            var clicked = (button & 0x8000) != 0 || (button & 0x0001) != 0;

            if (clicked && element is not null)
            {
                _logger.LogInformation("SAP UI Spy: element seçildi {ElementId} ({Type}).", element.Id, element.Type);
                return element;
            }

            await Task.Delay(40, cancellationToken);
        }
    }

    private static (int X, int Y) CursorPosition()
        => GetCursorPos(out var p) ? (p.X, p.Y) : (0, 0);

    private static bool IsKeyDown(int vKey) => (GetAsyncKeyState(vKey) & 0x8000) != 0;

    /// <summary>Seçenekte istenen modifier'lar (Ctrl/Shift/Alt) şu an basılı mı?</summary>
    private static bool ModifiersHeld(ImagePickerOptions options)
        => (!options.Ctrl || IsKeyDown(VkControl))
           && (!options.Shift || IsKeyDown(VkShift))
           && (!options.Alt || IsKeyDown(VkMenu));

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out PickerPoint lpPoint);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct PickerPoint
    {
        public int X;
        public int Y;
    }
}

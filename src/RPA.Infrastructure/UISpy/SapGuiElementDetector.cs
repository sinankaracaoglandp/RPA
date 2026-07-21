namespace RPA.Infrastructure.UISpy;

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using RPA.Domain.ValueObjects;

/// <summary>
/// İmleç altındaki ekran noktasından SAP GUI elementini çıkaran düşük seviye sağlayıcı soyutlaması.
/// Windows P/Invoke (GetCursorPos, WindowFromPoint, GetClassName) gerçek implementasyonda kullanılır;
/// birim testlerde mock'lanır.
/// </summary>
public interface INativeWindowApi
{
    /// <summary>Global imleç konumunu (ekran koordinatı) döner.</summary>
    (int X, int Y) GetCursorPosition();

    /// <summary>Verilen ekran noktasındaki pencerenin sınıf adını döner (yoksa null).</summary>
    string? GetWindowClassAt(int x, int y);

    /// <summary>
    /// Verilen ekran noktasındaki pencerenin KÖK (top-level) sınıf adını döner.
    /// <para><c>WindowFromPoint</c> noktanın altındaki ALT kontrolü verir; SAP metin alanının
    /// üzerindeyken dönen sınıf <c>SAP_FRONTEND*</c> DEĞİLDİR. SAP penceresi tespiti bu yüzden
    /// kök pencereye bakmalıdır.</para>
    /// Varsayılan implementasyon geriye uyumluluk için alt pencere sınıfına düşer.
    /// </summary>
    string? GetRootWindowClassAt(int x, int y) => GetWindowClassAt(x, y);
}

/// <summary>
/// Bir ekran noktasındaki SAP GUI Scripting element hiyerarşisini (wnd[]/usr/...) çözen soyutlama.
/// Gerçek implementasyon <c>sapfewse.ocx</c> COM nesneleri (GuiSession.FindByPosition) üzerinden
/// çalışır ve yalnızca SAP GUI kurulu Windows makinede geçerlidir; testlerde mock/stub'lanır.
/// </summary>
public interface ISapGuiElementResolver
{
    /// <summary>Verilen ekran noktasındaki SAP elementini döner (SAP dışıysa veya bulunamazsa null).</summary>
    SapGuiElement? ResolveAt(int x, int y);

    /// <summary>
    /// Verilen noktadaki elementi kullanıcıya görsel olarak vurgular (UI Spy hover geri bildirimi).
    /// Varsayılan olarak işlemsizdir — vurgu opsiyonel bir konfordur, seçim doğruluğunu etkilemez.
    /// </summary>
    void Highlight(int x, int y) { }

    /// <summary>
    /// Son çizilen vurgu çerçevesini kaldırır. SAP'ın <c>Visualize(true)</c> çerçevesi kendiliğinden
    /// silinmez; kapatılmazsa gezinirken ekranda çerçeveler birikir.
    /// </summary>
    void ClearHighlight() { }

    /// <summary>
    /// Son çözümleme hatasının açıklaması (yoksa null). UI Spy tanılaması için: element
    /// bulunamadığında sebebin (COM attach / scripting izni / nokta SAP dışı) kullanıcıya
    /// bildirilebilmesi gerekir.
    /// </summary>
    string? LastError => null;

    /// <summary>
    /// İmleç konumundan BAĞIMSIZ bağlantı öz-testi: SAP'a attach olunabiliyor mu, kaç oturum var,
    /// ana pencere (wnd[0]) okunabiliyor mu ve ekranda hangi dikdörtgeni kaplıyor?
    /// Picker başlangıcında loglanır — kullanıcı fareyi SAP'tan çekmeden sorunu görebilsin diye.
    /// </summary>
    string SelfTest() => "öz-test bu çözücüde desteklenmiyor";
}

/// <summary>
/// UI Spy element tespit motoru (Spec Bölüm 6). İmleç altındaki pencerenin SAP GUI penceresi olup
/// olmadığını kontrol eder; öyleyse SAP COM hiyerarşisinden hiyerarşik ID'li (<c>wnd[0]/usr/...</c>)
/// bir <see cref="SapGuiElement"/> çıkarır. SAP penceresi değilse <c>null</c> döner.
///
/// Windows-only: P/Invoke ve SAP COM interop yalnızca Windows'ta çalışır. Native ve COM erişimi
/// <see cref="INativeWindowApi"/> / <see cref="ISapGuiElementResolver"/> ardında soyutlanır; bu
/// sayede tespit mantığı SAP GUI kurulumu olmadan birim testlerle doğrulanabilir.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SapGuiElementDetector
{
    // SAP GUI ana oturum penceresinin Win32 sınıf adı öneki (SAP_FRONTEND_SESSION / SAP_FRONTEND_*).
    private const string SapWindowClassPrefix = "SAP_FRONTEND";

    private readonly INativeWindowApi _nativeApi;
    private readonly ISapGuiElementResolver _resolver;
    private readonly ILogger<SapGuiElementDetector> _logger;

    public SapGuiElementDetector(
        INativeWindowApi nativeApi,
        ISapGuiElementResolver resolver,
        ILogger<SapGuiElementDetector> logger)
    {
        _nativeApi = nativeApi ?? throw new ArgumentNullException(nameof(nativeApi));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// İmleç konumunu okur ve altındaki SAP GUI elementini döner. İmleç bir SAP GUI penceresinin
    /// üzerinde değilse <c>null</c> döner.
    /// </summary>
    public SapGuiElement? DetectElementUnderCursor()
    {
        var (x, y) = _nativeApi.GetCursorPosition();
        return DetectElementAt(x, y);
    }

    /// <summary>Belirli bir ekran noktasındaki SAP GUI elementini döner (on-demand tespit).</summary>
    public SapGuiElement? DetectElementAt(int x, int y)
    {
        // Pencere sınıfına bakarak "burası SAP mi?" diye karar VERMİYORUZ. WindowFromPoint en
        // derin child'ı verir; SAP ekranında bu bir alt kontrol ('Edit'), açık bir menü ('#32768')
        // veya popup olabilir ve hiçbiri 'SAP_FRONTEND*' değildir. Sınıf kapısı bu yüzden sürekli
        // YANLIŞ NEGATİF üretiyordu (picker hiçbir zaman element bulamıyordu).
        //
        // Doğru otorite SAP'ın kendisidir: FindByPosition bir bileşen döndürüyorsa nokta zaten
        // SAP oturumundadır. Sınıf bilgisi yalnızca tanılamada (Diagnose) kullanılır.
        var element = _resolver.ResolveAt(x, y);
        if (element is null)
        {
            _logger.LogDebug("UI Spy: ({X},{Y}) SAP penceresi ama element çözülemedi.", x, y);
            return null;
        }

        // Tespit anındaki konumu elementle birlikte döndür.
        var located = element with { X = x, Y = y };
        _logger.LogDebug("UI Spy: element tespit edildi {ElementId} ({Type}) @ ({X},{Y}).", located.Id, located.Type, x, y);
        return located;
    }

    /// <summary>
    /// Verilen noktada element bulunamamasının SEBEBİNİ insan-okur biçimde döner (tanılama).
    /// Picker, seçim yapılamadığında bunu loglar — aksi halde kullanıcı "hiçbir şey olmuyor"
    /// dışında bir bilgi alamaz.
    /// </summary>
    public string Diagnose(int x, int y)
    {
        var childClass = _nativeApi.GetWindowClassAt(x, y);
        var rootClass = _nativeApi.GetRootWindowClassAt(x, y);
        var isSap = IsSapWindow(childClass) || IsSapWindow(rootClass);

        var windows = $"child: '{childClass ?? "<null>"}', kök: '{rootClass ?? "<null>"}'" +
                      (isSap ? " (SAP penceresi)" : string.Empty);

        var error = _resolver.LastError;
        return error is null
            ? $"({x},{y}) bu noktada SAP elementi yok — {windows}. " +
              "İmleç SAP oturum penceresinin DIŞINDAYSA bu normaldir. SAP ekranının üzerindeyken de " +
              "oluyorsa nokta hiçbir SAP penceresinin dikdörtgenine düşmüyor demektir."
            : $"({x},{y}) element çözülemedi: {error} — {windows}";
    }

    /// <summary>SAP bağlantı öz-testi (imleçten bağımsız) — picker başlangıcında loglanır.</summary>
    public string SelfTest() => _resolver.SelfTest();

    /// <summary>UI Spy hover geri bildirimi: verilen noktadaki SAP elementini vurgular (best-effort).</summary>
    public void HighlightAt(int x, int y) => _resolver.Highlight(x, y);

    /// <summary>Ekranda kalan son vurgu çerçevesini kaldırır.</summary>
    public void ClearHighlight() => _resolver.ClearHighlight();

    /// <summary>Pencere sınıf adının bir SAP GUI penceresine ait olup olmadığını belirler.</summary>
    public static bool IsSapWindow(string? windowClass)
        => !string.IsNullOrWhiteSpace(windowClass)
           && windowClass.StartsWith(SapWindowClassPrefix, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// SAP GUI kurulmamış makinelerde (ve birim testlerde) kullanılan deterministik resolver stub'ı.
/// Gerçek COM resolver'ı (GuiSession.FindByPosition) SAP GUI kurulu Windows'ta yerini alır; entegrasyon
/// testinde (real DEP) doğrulanır. Stub, herhangi bir noktada element bulunamadığını (null) bildirir.
/// </summary>
public sealed class NullSapGuiElementResolver : ISapGuiElementResolver
{
    public SapGuiElement? ResolveAt(int x, int y) => null;
}

/// <summary>
/// <see cref="INativeWindowApi"/>'nin gerçek Windows P/Invoke implementasyonu (Spec Bölüm 6).
/// <c>GetCursorPos</c> + <c>WindowFromPoint</c> + <c>GetClassName</c> user32 çağrılarını sarmalar.
/// Yalnızca Windows'ta çalışır.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class Win32NativeWindowApi : INativeWindowApi
{
    public (int X, int Y) GetCursorPosition()
    {
        if (GetCursorPos(out var pt))
        {
            return (pt.X, pt.Y);
        }

        return (0, 0);
    }

    public string? GetWindowClassAt(int x, int y)
        => ClassNameOf(WindowFromPoint(new POINT { X = x, Y = y }));

    public string? GetRootWindowClassAt(int x, int y)
    {
        var hWnd = WindowFromPoint(new POINT { X = x, Y = y });
        if (hWnd == IntPtr.Zero)
        {
            return null;
        }

        // GA_ROOT (2): alt kontrolden top-level pencereye çık.
        var root = GetAncestor(hWnd, 2);
        return ClassNameOf(root == IntPtr.Zero ? hWnd : root);
    }

    private static string? ClassNameOf(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return null;
        }

        var buffer = new char[256];
        var length = GetClassName(hWnd, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : null;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(IntPtr hWnd, char[] lpClassName, int nMaxCount);
}

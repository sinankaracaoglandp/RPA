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
        var windowClass = _nativeApi.GetWindowClassAt(x, y);
        if (!IsSapWindow(windowClass))
        {
            _logger.LogDebug("UI Spy: ({X},{Y}) noktası SAP penceresi değil (sınıf: {Class}).", x, y, windowClass ?? "<null>");
            return null;
        }

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
    {
        var hWnd = WindowFromPoint(new POINT { X = x, Y = y });
        if (hWnd == IntPtr.Zero)
        {
            return null;
        }

        var buffer = new char[256];
        var length = GetClassName(hWnd, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : null;
    }

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

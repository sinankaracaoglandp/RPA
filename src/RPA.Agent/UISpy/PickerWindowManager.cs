namespace RPA.Agent.UISpy;

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

/// <summary>
/// Picker'ların "tek ekran" davranışı: seçim süresince öndeki pencereyi (tasarımcı tarayıcısı)
/// küçültür, seçim/iptal bitince tam olarak eski yerleşimine (maximized/normal + konum) getirir.
/// Testlerde no-op ile değiştirilebilsin diye soyutlanmıştır.
/// </summary>
public interface IPickerWindowManager
{
    /// <summary>Öndeki pencereyi küçültür; döndürülen eylem onu eski hâline getirir.</summary>
    Action MinimizeForeground();
}

/// <summary>Pencereye dokunmayan implementasyon (Windows dışı ortamlar ve birim testleri).</summary>
public sealed class NoopPickerWindowManager : IPickerWindowManager
{
    public Action MinimizeForeground() => static () => { };
}

/// <summary><see cref="IPickerWindowManager"/>'ın Win32 (user32) implementasyonu.</summary>
[SupportedOSPlatform("windows")]
public sealed class Win32PickerWindowManager : IPickerWindowManager
{
    private const int SwMinimize = 6;

    private readonly ILogger<Win32PickerWindowManager> _logger;

    public Win32PickerWindowManager(ILogger<Win32PickerWindowManager> logger)
        => _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public Action MinimizeForeground()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return static () => { };
        }

        var saved = CapturePlacement(hwnd);

        try { ShowWindow(hwnd, SwMinimize); }
        catch (Exception ex) { _logger.LogTrace(ex, "Picker: pencere küçültülemedi."); }

        return () => Restore(hwnd, saved);
    }

    private WINDOWPLACEMENT? CapturePlacement(IntPtr hwnd)
    {
        try
        {
            var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
            return GetWindowPlacement(hwnd, ref placement) ? placement : null;
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Picker: pencere yerleşimi okunamadı.");
            return null;
        }
    }

    private void Restore(IntPtr hwnd, WINDOWPLACEMENT? placement)
    {
        try
        {
            if (placement is WINDOWPLACEMENT saved)
            {
                var p = saved;
                SetWindowPlacement(hwnd, ref p);
            }
            SetForegroundWindow(hwnd);
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Picker: pencere geri getirilemedi.");
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT
    {
        public int length;
        public int flags;
        public int showCmd;
        public POINT ptMinPosition;
        public POINT ptMaxPosition;
        public RECT rcNormalPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }
}

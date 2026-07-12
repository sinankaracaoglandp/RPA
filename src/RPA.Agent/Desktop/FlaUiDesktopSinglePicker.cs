namespace RPA.Agent.Desktop;

using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Microsoft.Extensions.Logging;
using RPA.Agent.UISpy;
using RPA.Domain.ValueObjects;

/// <summary>
/// <see cref="IDesktopSinglePicker"/>'ın FlaUI (UIA3) implementasyonu — DesktopSpy tek-seçim kipi
/// (Spec Bölüm 5, Paket E). İmleç altındaki UIA elementini <c>FromPoint</c> ile bulur, çerçeveyle
/// vurgular; kullanıcı sol tıklamayla seçimi onaylar, <c>Esc</c> ile iptal eder.
///
/// <para>Üretilen selector tek segmentlidir ve
/// <see cref="FlaUiDesktopAutomationChannel"/> çözümleyicisiyle uyumludur:
/// <c>ControlType[AutomationId='x']</c> (yoksa <c>[Name='x']</c>).</para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class FlaUiDesktopSinglePicker : IDesktopSinglePicker, IDisposable
{
    private const int VkLButton = 0x01;
    private const int VkEscape = 0x1B;

    private readonly ILogger<FlaUiDesktopSinglePicker> _logger;
    private readonly UIA3Automation _automation = new();

    public FlaUiDesktopSinglePicker(ILogger<FlaUiDesktopSinglePicker> logger)
        => _logger = logger;

    public async Task<DesktopUiElement?> DetectOnceAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("DesktopSpy: tek-seçim başladı — hedefe sol tıklayın, Esc iptal.");
        AutomationElement? lastHighlighted = null;

        // Tek ekran kullanımı: öndeki pencere (genelde tasarımcı tarayıcısı) hedef uygulamayı
        // kapatır. Seçim öncesi tam yerleşimini kaydeder, küçültür; bittiğinde birebir
        // (maximized/normal + konum) geri getiririz.
        var foreground = GetForegroundWindow();
        var savedPlacement = CapturePlacement(foreground);
        MinimizeWindow(foreground);

        try
        {
            // Butonun serbest bırakılmış halde başladığından emin ol (önceki tıklama sızmasın).
            while (IsKeyDown(VkLButton))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(20, cancellationToken);
            }
            // Bekleme sırasında birikmiş "basıldı" bitini temizle (ilk turda yanlış tetiklenmesin).
            GetAsyncKeyState(VkLButton);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (IsKeyDown(VkEscape))
                {
                    _logger.LogDebug("DesktopSpy: kullanıcı Esc ile iptal etti.");
                    return null;
                }

                var (x, y) = CursorPosition();
                var element = SafeFromPoint(x, y);
                if (element is not null && !ReferenceEquals(element, lastHighlighted))
                {
                    TryHighlight(element);
                    lastHighlighted = element;
                }

                // Sol buton durumunu tur başına TEK kez oku: 0x8000 = şu an basılı,
                // 0x0001 = son okumadan bu yana basıldı (hızlı tık aksi halde 40 ms yoklama
                // araları arasına düşüp kaçırılır). Her iki bit de tıklama sayılır.
                var lButton = GetAsyncKeyState(VkLButton);
                var clicked = (lButton & 0x8000) != 0 || (lButton & 0x0001) != 0;
                if (clicked && element is not null)
                {
                    var result = Describe(element, x, y);
                    _logger.LogInformation("DesktopSpy: element seçildi {UiaPath}", result.UiaPath);
                    return result;
                }

                await Task.Delay(40, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            // Seçim bitince tasarımcı penceresini seçim öncesi konum/durumuna getir.
            RestoreWindow(foreground, savedPlacement);
        }
    }

    private AutomationElement? SafeFromPoint(int x, int y)
    {
        try
        {
            return _automation.FromPoint(new Point(x, y));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "DesktopSpy: FromPoint başarısız ({X},{Y}).", x, y);
            return null;
        }
    }

    private void TryHighlight(AutomationElement element)
    {
        try
        {
            element.DrawHighlight(blocking: false, Color.OrangeRed, TimeSpan.FromMilliseconds(400));
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "DesktopSpy: highlight çizilemedi.");
        }
    }

    private static DesktopUiElement Describe(AutomationElement element, int x, int y)
    {
        string? automationId = SafeGet(() => element.AutomationId);
        string? name = SafeGet(() => element.Name);
        string? controlType = SafeGet(() => element.Properties.ControlType.ValueOrDefault.ToString());
        string? processName = SafeGet(() =>
        {
            var pid = element.Properties.ProcessId.ValueOrDefault;
            return pid > 0 ? System.Diagnostics.Process.GetProcessById(pid).ProcessName : null;
        });

        var type = string.IsNullOrWhiteSpace(controlType) ? "Custom" : controlType;
        string selector;
        if (!string.IsNullOrWhiteSpace(automationId))
        {
            selector = $"{type}[AutomationId='{automationId}']";
        }
        else if (!string.IsNullOrWhiteSpace(name))
        {
            selector = $"{type}[Name='{Escape(name)}']";
        }
        else
        {
            selector = type;
        }

        return new DesktopUiElement
        {
            UiaPath = selector,
            AutomationId = automationId,
            ControlType = controlType,
            Name = name,
            ProcessName = processName,
            X = x,
            Y = y,
        };
    }

    private static string Escape(string value) => value.Replace("'", string.Empty);

    private static string? SafeGet(Func<string?> getter)
    {
        try { return getter(); }
        catch { return null; }
    }

    private static (int X, int Y) CursorPosition()
        => GetCursorPos(out var p) ? (p.X, p.Y) : (0, 0);

    private static bool IsKeyDown(int vKey) => (GetAsyncKeyState(vKey) & 0x8000) != 0;

    private const int SwMinimize = 6;

    private WINDOWPLACEMENT? CapturePlacement(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }
        try
        {
            var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
            return GetWindowPlacement(hwnd, ref placement) ? placement : null;
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "DesktopSpy: pencere yerleşimi okunamadı.");
            return null;
        }
    }

    private void MinimizeWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }
        try { ShowWindow(hwnd, SwMinimize); }
        catch (Exception ex) { _logger.LogTrace(ex, "DesktopSpy: pencere küçültülemedi."); }
    }

    private void RestoreWindow(IntPtr hwnd, WINDOWPLACEMENT? placement)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }
        try
        {
            if (placement is WINDOWPLACEMENT saved)
            {
                var p = saved;
                SetWindowPlacement(hwnd, ref p); // maximized/normal + konum birebir
            }
            SetForegroundWindow(hwnd);
        }
        catch (Exception ex) { _logger.LogTrace(ex, "DesktopSpy: pencere geri getirilemedi."); }
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    public void Dispose() => _automation.Dispose();
}

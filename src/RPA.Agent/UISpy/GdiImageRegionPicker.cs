namespace RPA.Agent.UISpy;

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Forms;
using RPA.Agent.Vision;

/// <summary>
/// GDI overlay ile ekranda dikdörtgen bölge seçtiren image picker. Geçici menü/pencere yakalamak
/// için iki aşamalıdır: (1) "arm" — kullanıcı hedef UI'ı elle açar, F2 ile (veya zamanlayıcıyla)
/// ekranı dondurur; (2) donmuş görüntü üzerinde dikdörtgen seçimi. Kırpma canlı ekrandan değil
/// donmuş fotoğraftan yapılır → dikdörtgen çizerken menü kapansa bile görüntü korunur.
/// Seçilen bölgenin PNG'sini base64 ve {x,y,width,height} JSON'unu (mutlak sanal-ekran koordinatı)
/// döndürür. İptal → null.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GdiImageRegionPicker : IImageRegionPicker
{
    public Task<ImagePick?> DetectOnceAsync(ImagePickerOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = ImagePickerSession.Run(options, cancellationToken);
        return Task.FromResult(result);
    }
}

/// <summary>
/// Tek bir STA thread üzerinde arm + seçim akışını yürütür. Freeze anındaki görüntü Bitmap olarak
/// tutulur; seçim bu donmuş görüntü üzerinde yapılır ve kırpma ondan üretilir.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ImagePickerSession
{
    public static ImagePick? Run(ImagePickerOptions options, CancellationToken cancellationToken)
    {
        ImagePick? result = null;

        var thread = new Thread(() =>
        {
            // 1) Arm: hedef UI açılana kadar bekle, dondur anında ekran görüntüsünü al.
            Bitmap? snapshot = ArmForm.WaitAndCapture(options, cancellationToken);
            if (snapshot is null)
            {
                return; // iptal / timeout
            }

            using (snapshot)
            {
                var (ox, oy) = ScreenCapture.VirtualScreenOrigin;

                // 2) Donmuş görüntü üzerinde bölge seçimi (client koordinatı).
                var rect = SelectionForm.SelectOnSnapshot(snapshot, cancellationToken);
                if (rect is null)
                {
                    return; // Esc / geçersiz seçim → iptal
                }

                var r = ClampToBitmap(rect.Value, snapshot);
                if (r.Width < 2 || r.Height < 2)
                {
                    return;
                }

                // 3) Donmuş görüntüden kırp → PNG → base64. Region mutlak sanal-ekran koordinatı.
                using var cropped = snapshot.Clone(r, snapshot.PixelFormat);
                using var ms = new MemoryStream();
                cropped.Save(ms, ImageFormat.Png);
                var base64 = Convert.ToBase64String(ms.ToArray());
                var regionJson =
                    $"{{\"x\":{ox + r.X},\"y\":{oy + r.Y},\"width\":{r.Width},\"height\":{r.Height}}}";
                result = new ImagePick(base64, regionJson);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        return result;
    }

    private static Rectangle ClampToBitmap(Rectangle rect, Bitmap bmp)
    {
        var x = Math.Clamp(rect.X, 0, bmp.Width);
        var y = Math.Clamp(rect.Y, 0, bmp.Height);
        var w = Math.Clamp(rect.Width, 0, bmp.Width - x);
        var h = Math.Clamp(rect.Height, 0, bmp.Height - y);
        return new Rectangle(x, y, w, h);
    }
}

/// <summary>
/// Küçük, odak çalmayan (WS_EX_NOACTIVATE) bilgi çubuğu. F2 modunda global F2 kısayolunu bekler;
/// zamanlayıcı modunda geri sayar. Tetiklenince kendini gizleyip ekranı dondurur (Bitmap döndürür).
/// Hedef uygulamanın (SAP menüsü) odağını çalmadığı için açılan menü açık kalır.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ArmForm : Form
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0xF2A1;

    private readonly ImagePickerOptions _options;
    private readonly Label _label = new();
    private readonly System.Windows.Forms.Timer _timer = new();
    private int _remaining;
    private bool _hotkeyRegistered;

    public Bitmap? Snapshot { get; private set; }

    private ArmForm(ImagePickerOptions options)
    {
        _options = options;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = Color.FromArgb(28, 28, 30);
        Size = new Size(520, 46);

        var wa = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Bottom - Height - 24);

        _label.Dock = DockStyle.Fill;
        _label.ForeColor = Color.White;
        _label.TextAlign = ContentAlignment.MiddleCenter;
        _label.Font = new Font("Segoe UI", 10F, FontStyle.Regular);
        Controls.Add(_label);

        _timer.Interval = 1000;
        _timer.Tick += OnTick;
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // İmlecin bulunduğu monitörün altına yerleştir (kullanıcı hangi ekranda çalışıyorsa orada görünsün).
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Bottom - Height - 24);
        // Odağı çalmadan (SAP menüsü açık kalsın) topmost z-sırasının en üstüne getir; aksi halde
        // SAP ön plandayken çubuk arkada kalıp hiç görünmüyordu.
        NativeForeground.RaiseTopMostNoActivate(Handle);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int WsExTopmost = 0x00000008;
            const int WsExNoActivate = 0x08000000;
            const int WsExToolWindow = 0x00000080;
            var cp = base.CreateParams;
            cp.ExStyle |= WsExTopmost | WsExNoActivate | WsExToolWindow;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (string.Equals(_options.CaptureMode, ImagePickerOptions.ModeTimer, StringComparison.OrdinalIgnoreCase))
        {
            _remaining = _options.DelaySeconds;
            _label.Text = BuildTimerText(_remaining);
            _timer.Start();
        }
        else
        {
            var combo = _options.DisplayCombo;
            _hotkeyRegistered = RegisterHotKey(Handle, HotkeyId, _options.Modifiers, _options.VirtualKey);
            _label.Text = _hotkeyRegistered
                ? $"Hedef menüyü/pencereyi açın, sonra  {combo}  ile dondurun."
                : $"Hedef menüyü açın… ({combo} kısayolu kaydedilemedi; birazdan otomatik donacak)";
            if (!_hotkeyRegistered)
            {
                // Kısayol kaydı başarısızsa (başka uygulama tutuyorsa) güvenli geri dönüş: kısa zamanlayıcı.
                _remaining = Math.Max(3, _options.DelaySeconds);
                _timer.Start();
            }
        }
    }

    private static string BuildTimerText(int remaining) =>
        $"Hedef menüyü/pencereyi açın — {remaining} sn sonra donacak…";

    private void OnTick(object? sender, EventArgs e)
    {
        _remaining--;
        if (_remaining <= 0)
        {
            FreezeAndClose();
            return;
        }
        _label.Text = BuildTimerText(_remaining);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey && m.WParam.ToInt32() == HotkeyId)
        {
            FreezeAndClose();
            return;
        }
        base.WndProc(ref m);
    }

    private void FreezeAndClose()
    {
        _timer.Stop();
        // Bilgi çubuğunu gizle ki fotoğrafa girmesin; ekranın güncellenmesine izin ver.
        Hide();
        Application.DoEvents();
        Thread.Sleep(80);
        try
        {
            Snapshot = ScreenCapture.CaptureVirtualScreenBitmap();
        }
        finally
        {
            Close();
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (_hotkeyRegistered)
        {
            UnregisterHotKey(Handle, HotkeyId);
            _hotkeyRegistered = false;
        }
        _timer.Stop();
        _timer.Dispose();
        base.OnFormClosed(e);
    }

    /// <summary>Arm akışını çalıştırır; dondurulan ekranın Bitmap'ini döndürür, iptalde null.</summary>
    public static Bitmap? WaitAndCapture(ImagePickerOptions options, CancellationToken cancellationToken)
    {
        using var form = new ArmForm(options);
        using var reg = cancellationToken.Register(() =>
        {
            try
            {
                if (form.IsHandleCreated)
                {
                    form.BeginInvoke(new Action(form.Close));
                }
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        });

        Application.Run(form);
        return form.Snapshot;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

/// <summary>
/// Donmuş ekran görüntüsünü tüm monitörleri kaplayan bir form üzerinde gösterir; fareyle dikdörtgen
/// çizip bırakınca seçimi (client koordinatı) döndürür, Esc iptal eder. Client (0,0) = sanal ekran
/// sol-üstü olduğundan client dikdörtgeni doğrudan snapshot piksel dikdörtgenidir.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class SelectionForm : Form
{
    private Point _start;
    private Point _current;
    private bool _dragging;

    public Rectangle? SelectedClientRect { get; private set; }

    private SelectionForm(Bitmap snapshot)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = SystemInformation.VirtualScreen;
        TopMost = true;
        DoubleBuffered = true;
        Cursor = Cursors.Cross;
        ShowInTaskbar = false;
        KeyPreview = true;
        BackgroundImage = snapshot;
        BackgroundImageLayout = ImageLayout.None;

        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        KeyDown += OnKeyDown;
        Paint += OnPaint;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // Ekran donduruldu; seçim katmanını zorla ön plana al (SAP menüsü açıkken foreground kilidi
        // yüzünden arkada kalıp fare girdisi alamıyordu). Menü snapshot'a alındığı için kapanması sorun değil.
        NativeForeground.ForceForeground(Handle);
        Activate();
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        _start = e.Location;
        _current = e.Location;
        _dragging = true;
        Invalidate();
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }
        _current = e.Location;
        Invalidate();
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }
        _dragging = false;
        _current = e.Location;

        var rect = NormalizedRect(_start, _current);
        if (rect.Width > 2 && rect.Height > 2)
        {
            SelectedClientRect = rect;
        }
        Close();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            SelectedClientRect = null;
            Close();
        }
    }

    private void OnPaint(object? sender, PaintEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }
        var rect = NormalizedRect(_start, _current);

        // Seçim dışını hafifçe karart (donmuş görüntü net kalsın, seçim vurgulansın).
        using (var dim = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
        {
            var full = ClientRectangle;
            e.Graphics.FillRectangle(dim, full.X, full.Y, full.Width, rect.Top - full.Y);
            e.Graphics.FillRectangle(dim, full.X, rect.Bottom, full.Width, full.Bottom - rect.Bottom);
            e.Graphics.FillRectangle(dim, full.X, rect.Top, rect.Left - full.X, rect.Height);
            e.Graphics.FillRectangle(dim, rect.Right, rect.Top, full.Right - rect.Right, rect.Height);
        }

        using var pen = new Pen(Color.OrangeRed, 2);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawRectangle(pen, rect);
    }

    private static Rectangle NormalizedRect(Point a, Point b)
    {
        var x = Math.Min(a.X, b.X);
        var y = Math.Min(a.Y, b.Y);
        var w = Math.Abs(a.X - b.X);
        var h = Math.Abs(a.Y - b.Y);
        return new Rectangle(x, y, w, h);
    }

    /// <summary>Donmuş görüntü üzerinde bölge seçtirir; seçilen client dikdörtgenini döndürür (iptalde null).</summary>
    public static Rectangle? SelectOnSnapshot(Bitmap snapshot, CancellationToken cancellationToken)
    {
        using var form = new SelectionForm(snapshot);
        using var reg = cancellationToken.Register(() =>
        {
            try
            {
                if (form.IsHandleCreated)
                {
                    form.BeginInvoke(new Action(form.Close));
                }
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        });

        Application.Run(form);
        return form.SelectedClientRect;
    }
}

/// <summary>
/// Pencereyi ön plana / topmost z-sırasına getiren P/Invoke yardımcıları. SAP gibi başka bir
/// uygulama ön plandayken (özellikle açık menü varken) Windows foreground kilidini aşmak için
/// AttachThreadInput hilesi kullanılır.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class NativeForeground
{
    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;

    /// <summary>Odağı çalmadan pencereyi topmost z-sırasının en üstüne çıkarır (arm çubuğu için).</summary>
    public static void RaiseTopMostNoActivate(IntPtr hWnd)
    {
        SetWindowPos(hWnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
    }

    /// <summary>Pencereyi zorla ön plana getirir (seçim katmanı için); foreground kilidini aşar.</summary>
    public static void ForceForeground(IntPtr hWnd)
    {
        var foreground = GetForegroundWindow();
        var foregroundThread = GetWindowThreadProcessId(foreground, IntPtr.Zero);
        var thisThread = GetCurrentThreadId();

        var attached = false;
        if (foregroundThread != 0 && foregroundThread != thisThread)
        {
            attached = AttachThreadInput(thisThread, foregroundThread, true);
        }

        RaiseTopMostNoActivate(hWnd);
        BringWindowToTop(hWnd);
        SetForegroundWindow(hWnd);

        if (attached)
        {
            AttachThreadInput(thisThread, foregroundThread, false);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}

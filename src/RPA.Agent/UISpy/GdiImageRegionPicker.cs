namespace RPA.Agent.UISpy;

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.Versioning;
using System.Windows.Forms;
using RPA.Agent.Vision;

/// <summary>
/// GDI overlay ile ekranda dikdörtgen bölge seçtiren image picker. Seçilen bölgenin PNG'sini
/// base64 olarak ve {x,y,width,height} JSON'unu döndürür. Esc → iptal (null).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GdiImageRegionPicker : IImageRegionPicker
{
    public Task<ImagePick?> DetectOnceAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rect = RegionOverlayForm.SelectRegion(cancellationToken); // null → iptal
        if (rect is null)
        {
            return Task.FromResult<ImagePick?>(null);
        }
        var (x, y, w, h) = rect.Value;
        using var mat = ScreenCapture.Capture(x, y, w, h);
        var png = mat.ImEncode(".png");
        var base64 = Convert.ToBase64String(png);
        var regionJson = $"{{\"x\":{x},\"y\":{y},\"width\":{w},\"height\":{h}}}";
        return Task.FromResult<ImagePick?>(new ImagePick(base64, regionJson));
    }
}

/// <summary>
/// Tüm monitörleri (sanal ekran) kaplayan, yarı saydam WinForms overlay — fareyle dikdörtgen
/// çizip bırakınca seçimi onaylar; Esc iptal eder. Seçim mutlak sanal-ekran koordinatı olarak
/// döner (çoklu monitör). STA thread gerektirir (FlaUiDesktopSinglePicker deseniyle aynı biçimde
/// ayrı bir STA thread'de gösterilir).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class RegionOverlayForm
{
    public static (int X, int Y, int Width, int Height)? SelectRegion(CancellationToken cancellationToken)
    {
        (int X, int Y, int Width, int Height)? result = null;

        var thread = new Thread(() =>
        {
            using var form = new OverlayForm();
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
            result = form.SelectedRegion;
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        return result;
    }

    private sealed class OverlayForm : Form
    {
        private Point _start;
        private Point _current;
        private bool _dragging;

        public (int X, int Y, int Width, int Height)? SelectedRegion { get; private set; }

        public OverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            // Tüm monitörleri kapsayan sanal ekranı kapla (Maximized yalnız tek monitörü kaplardı).
            StartPosition = FormStartPosition.Manual;
            Bounds = SystemInformation.VirtualScreen;
            TopMost = true;
            BackColor = Color.Black;
            Opacity = 0.3;
            DoubleBuffered = true;
            Cursor = Cursors.Cross;
            ShowInTaskbar = false;
            KeyPreview = true;

            MouseDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseUp += OnMouseUp;
            KeyDown += OnKeyDown;
            Paint += OnPaint;
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
                SelectedRegion = (Left + rect.X, Top + rect.Y, rect.Width, rect.Height);
            }
            Close();
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                SelectedRegion = null;
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
    }
}

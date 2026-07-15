namespace RPA.Agent.UISpy;

using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using System.Windows.Forms;
using OpenCvSharp.Extensions;
using RPA.Agent.Vision;
using RPA.Domain.ValueObjects;

/// <summary>
/// İki aşamalı görsel text-offset picker. GdiImageRegionPicker'ın freeze (ArmForm) + seçim
/// (SelectionForm) altyapısını yeniden kullanır: (1) ekranı dondur; (2) çapa etiketinin çevresine
/// dikdörtgen çiz → kırpıntı OCR edilir (çapa metni + tight kutu); (3) hedef noktaya tek tık →
/// dx/dy = tık − çapa kutusu merkezi. İptal (Esc / seçim yok / OCR boş) → null.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GdiTextOffsetPicker : ITextOffsetPicker
{
    private readonly string _tessdataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");

    public Task<TextOffsetPick?> DetectOnceAsync(ImagePickerOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TextOffsetPick? result = null;

        var thread = new Thread(() =>
        {
            using var snapshot = ArmForm.WaitAndCapture(options, cancellationToken);
            if (snapshot is null)
            {
                return; // iptal / timeout
            }

            // 1) Çapa dikdörtgeni (donmuş görüntü client koordinatı = snapshot pikseli).
            var anchorRect = SelectionForm.SelectOnSnapshot(snapshot, cancellationToken);
            if (anchorRect is null || anchorRect.Value.Width < 2 || anchorRect.Value.Height < 2)
            {
                return;
            }
            var ar = anchorRect.Value;

            // 2) Çapa kırpıntısını OCR et → en iyi (en geniş) kelime kutusu.
            string anchorText;
            VisionMatch anchorBoxOnSnapshot;
            using (var crop = snapshot.Clone(ar, snapshot.PixelFormat))
            using (var mat = BitmapConverter.ToMat(crop))
            {
                var (_, words) = OcrEngine.Read(mat, _tessdataPath, "tur+eng");
                var best = words
                    .Where(w => !string.IsNullOrWhiteSpace(w.Text))
                    .OrderByDescending(w => w.Box.Width * w.Box.Height)
                    .FirstOrDefault();
                if (best is null)
                {
                    return; // OCR metin bulamadı → iptal
                }
                anchorText = best.Text.Trim();
                // Kırpıntı-yerel kutuyu snapshot koordinatına ötele.
                anchorBoxOnSnapshot = new VisionMatch(
                    ar.X + best.Box.X, ar.Y + best.Box.Y, best.Box.Width, best.Box.Height, 1.0);
            }

            // 3) Hedef noktaya tek tık (aynı donmuş görüntü üzerinde).
            var target = ClickPointForm.PickPoint(snapshot, cancellationToken);
            if (target is null)
            {
                return;
            }

            var dx = target.Value.X - anchorBoxOnSnapshot.CenterX;
            var dy = target.Value.Y - anchorBoxOnSnapshot.CenterY;

            // Önizleme: çapa kırpıntısının PNG base64'ü.
            string preview;
            using (var crop = snapshot.Clone(ar, snapshot.PixelFormat))
            using (var ms = new MemoryStream())
            {
                crop.Save(ms, ImageFormat.Png);
                preview = Convert.ToBase64String(ms.ToArray());
            }

            result = new TextOffsetPick(anchorText, dx, dy, preview);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        return Task.FromResult(result);
    }
}

/// <summary>
/// Donmuş görüntü üzerinde tek nokta seçtiren form. Tıklanan client noktasını (snapshot pikseli)
/// döndürür, Esc iptal eder. SelectionForm ile aynı foreground/topmost hilelerini kullanır.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ClickPointForm : Form
{
    private Point? _picked;

    private ClickPointForm(Bitmap snapshot)
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

        MouseDown += (_, e) => { _picked = e.Location; Close(); };
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { _picked = null; Close(); } };
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        NativeForeground.ForceForeground(Handle);
        Activate();
    }

    public static Point? PickPoint(Bitmap snapshot, CancellationToken cancellationToken)
    {
        using var form = new ClickPointForm(snapshot);
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
        return form._picked;
    }
}

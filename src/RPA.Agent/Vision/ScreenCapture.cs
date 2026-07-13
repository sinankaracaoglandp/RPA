namespace RPA.Agent.Vision;

using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using OpenCvSharp;
using OpenCvSharp.Extensions;

/// <summary>GDI ile tam ekran veya bölge yakalar ve OpenCv Mat'e dönüştürür (BGR).
/// Tam ekran = tüm monitörleri kapsayan sanal ekran (çoklu monitör desteği).</summary>
[SupportedOSPlatform("windows")]
public static class ScreenCapture
{
    /// <summary>
    /// Tüm monitörleri kapsayan sanal ekranın sol-üst köşesi. Birincil monitör (0,0); soldaki/üstteki
    /// monitörler negatif olabilir. Tam-ekran yakalamanın (0,0) noktası bu koordinata denk gelir;
    /// template/OCR eşleşme koordinatı bu offset ile mutlak imleç konumuna çevrilir.
    /// </summary>
    public static (int X, int Y) VirtualScreenOrigin
    {
        get
        {
            var vs = System.Windows.Forms.SystemInformation.VirtualScreen;
            return (vs.X, vs.Y);
        }
    }

    public static Mat Capture(int? x, int? y, int? width, int? height)
    {
        // Varsayılan (null) = tüm monitörleri kapsayan sanal ekran (yalnız birincil değil).
        var vs = System.Windows.Forms.SystemInformation.VirtualScreen;
        var rx = x ?? vs.X;
        var ry = y ?? vs.Y;
        var rw = width ?? vs.Width;
        var rh = height ?? vs.Height;

        using var bmp = new Bitmap(rw, rh, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(rx, ry, 0, 0, new System.Drawing.Size(rw, rh));
        }
        return BitmapConverter.ToMat(bmp);
    }

    /// <summary>
    /// Tüm monitörleri kapsayan sanal ekranın anlık görüntüsünü <see cref="Bitmap"/> olarak alır
    /// (image picker'ın "dondur" akışı için; çağıran dispose eder). (0,0) = sanal ekran sol-üstü.
    /// </summary>
    public static Bitmap CaptureVirtualScreenBitmap()
    {
        var vs = System.Windows.Forms.SystemInformation.VirtualScreen;
        var bmp = new Bitmap(vs.Width, vs.Height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(vs.X, vs.Y, 0, 0, new System.Drawing.Size(vs.Width, vs.Height));
        }
        return bmp;
    }

    public static Mat DecodeBase64Png(string base64)
    {
        var bytes = Convert.FromBase64String(base64);
        return Cv2.ImDecode(bytes, ImreadModes.Color);
    }
}

namespace RPA.Agent.Vision;

using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using OpenCvSharp;
using OpenCvSharp.Extensions;

/// <summary>GDI ile tam ekran veya bölge yakalar ve OpenCv Mat'e dönüştürür (BGR).</summary>
[SupportedOSPlatform("windows")]
public static class ScreenCapture
{
    public static Mat Capture(int? x, int? y, int? width, int? height)
    {
        var bounds = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        var rx = x ?? bounds.X;
        var ry = y ?? bounds.Y;
        var rw = width ?? bounds.Width;
        var rh = height ?? bounds.Height;

        using var bmp = new Bitmap(rw, rh, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(rx, ry, 0, 0, new System.Drawing.Size(rw, rh));
        }
        return BitmapConverter.ToMat(bmp);
    }

    public static Mat DecodeBase64Png(string base64)
    {
        var bytes = Convert.FromBase64String(base64);
        return Cv2.ImDecode(bytes, ImreadModes.Color);
    }
}

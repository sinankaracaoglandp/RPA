namespace RPA.Agent.Vision;

using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
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

        using var bmp = CaptureBitmap(rx, ry, rw, rh);
        return BitmapConverter.ToMat(bmp);
    }

    /// <summary>
    /// Tüm monitörleri kapsayan sanal ekranın anlık görüntüsünü <see cref="Bitmap"/> olarak alır
    /// (image picker'ın "dondur" akışı için; çağıran dispose eder). (0,0) = sanal ekran sol-üstü.
    /// </summary>
    public static Bitmap CaptureVirtualScreenBitmap()
    {
        var vs = System.Windows.Forms.SystemInformation.VirtualScreen;
        return CaptureBitmap(vs.X, vs.Y, vs.Width, vs.Height);
    }

    public static Mat DecodeBase64Png(string base64)
    {
        var bytes = Convert.FromBase64String(base64);
        return Cv2.ImDecode(bytes, ImreadModes.Color);
    }

    /// <summary>
    /// Ekranın bir bölgesini GDI <c>BitBlt</c> ile <c>SRCCOPY | CAPTUREBLT</c> bayrağıyla yakalar.
    /// <see cref="Graphics.CopyFromScreen"/> yalnız <c>SRCCOPY</c> kullanır ve layered pencereleri
    /// (WS_EX_LAYERED — SAP/Win32 açılır menüleri, tooltip'ler, bazı popup'lar) yakalamaz;
    /// <c>CAPTUREBLT</c> bunları da dahil eder. Çağıran bitmap'i dispose eder.
    /// </summary>
    private static Bitmap CaptureBitmap(int x, int y, int width, int height)
    {
        var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        try
        {
            using var g = Graphics.FromImage(bmp);
            var hdcDest = g.GetHdc();
            var hdcSrc = GetDC(IntPtr.Zero);
            try
            {
                BitBlt(hdcDest, 0, 0, width, height, hdcSrc, x, y, SRCCOPY | CAPTUREBLT);
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, hdcSrc);
                g.ReleaseHdc(hdcDest);
            }
            return bmp;
        }
        catch
        {
            bmp.Dispose();
            throw;
        }
    }

    private const int SRCCOPY = 0x00CC0020;
    private const int CAPTUREBLT = 0x40000000;

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(
        IntPtr hdcDest, int xDest, int yDest, int width, int height,
        IntPtr hdcSrc, int xSrc, int ySrc, int rop);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
}

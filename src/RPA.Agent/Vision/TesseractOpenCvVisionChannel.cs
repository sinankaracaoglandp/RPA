namespace RPA.Agent.Vision;

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using RPA.Domain.Interfaces;
using RPA.Domain.ValueObjects;
using Tesseract;
using SystemException = RPA.Domain.Exceptions.SystemException;

/// <summary>
/// IVisionAutomationChannel'ın OpenCvSharp (template) + Tesseract (OCR) implementasyonu.
/// Etkileşimli masaüstü oturumu gerektirir. Bulunamadı/timeout → SystemException.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TesseractOpenCvVisionChannel : IVisionAutomationChannel
{
    private readonly ILogger<TesseractOpenCvVisionChannel> _logger;
    private readonly string _tessdataPath;

    public TesseractOpenCvVisionChannel(ILogger<TesseractOpenCvVisionChannel> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tessdataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");
    }

    public async Task ClickImageAsync(string imageBase64, double confidence, string? clickType, int timeoutMs)
    {
        var (match, bestScore, dumpPath) = await PollForImageAsync(imageBase64, confidence, timeoutMs);
        if (match is null)
        {
            throw new SystemException(ImageNotFoundMessage(confidence, bestScore, dumpPath));
        }
        DoClick(match.CenterX, match.CenterY, clickType);
    }

    public async Task WaitForImageAsync(string imageBase64, double confidence, int timeoutMs)
    {
        var (match, bestScore, dumpPath) = await PollForImageAsync(imageBase64, confidence, Math.Max(timeoutMs, 1));
        if (match is null)
        {
            throw new SystemException(ImageNotFoundMessage(confidence, bestScore, dumpPath));
        }
    }

    public async Task<bool> ImageExistsAsync(string imageBase64, double confidence, int timeoutMs)
        => (await PollForImageAsync(imageBase64, confidence, timeoutMs)).Match is not null;

    private static string ImageNotFoundMessage(double confidence, double bestScore, string? dumpPath) =>
        $"Ekran görüntüsü bulunamadı. Ulaşılan en iyi eşleşme skoru {bestScore:0.00}, eşik (confidence) {confidence:0.00}. " +
        "Menünün/pencerenin çalışma anında ekranda ve aynı görünümde (vurgusuz) olduğundan emin olun; " +
        "gerekirse 'confidence' değerini en iyi skorun biraz altına düşürün." +
        (dumpPath is null ? string.Empty : $" Robotun o an gördüğü ekran ve aranan görüntü buraya kaydedildi: {dumpPath}");

    public Task<string> GetTextAsync(int? x, int? y, int? width, int? height, string language)
    {
        using var region = ScreenCapture.Capture(x, y, width, height);
        var (text, _) = RunOcr(region, language);
        return Task.FromResult(text);
    }

    public async Task ClickTextAsync(string text, string language, string matchMode, string? clickType, int timeoutMs)
    {
        var box = await PollForTextAsync(text, language, matchMode, timeoutMs);
        if (box is null)
        {
            throw new SystemException($"Metin ekranda bulunamadı: '{text}' (timeout).");
        }
        DoClick(box.CenterX, box.CenterY, clickType);
    }

    public async Task<bool> TextExistsAsync(string text, string language, string matchMode, int timeoutMs)
        => await PollForTextAsync(text, language, matchMode, timeoutMs) is not null;

    public async Task ClickTextOffsetAsync(string anchorText, int dx, int dy,
        string language, string matchMode, string? clickType, int timeoutMs)
    {
        var box = await PollForTextAsync(anchorText, language, matchMode, timeoutMs);
        if (box is null)
        {
            throw new SystemException($"Çapa metni ekranda bulunamadı: '{anchorText}' (timeout).");
        }
        var (x, y) = VisionOffset.ClickPoint(box, dx, dy);
        DoClick(x, y, clickType);
    }

    private async Task<(VisionMatch? Match, double BestScore, string? DumpPath)> PollForImageAsync(string imageBase64, double confidence, int timeoutMs)
    {
        using var needle = ScreenCapture.DecodeBase64Png(imageBase64);
        var sw = Stopwatch.StartNew();
        var bestSeen = 0d;
        Mat? firstScreen = null;
        Mat? lastScreen = null;
        try
        {
            do
            {
                if (!ReferenceEquals(lastScreen, firstScreen))
                {
                    lastScreen?.Dispose();
                }
                lastScreen = ScreenCapture.Capture(null, null, null, null);
                // İlk kareyi sakla (node başında ekran durumu — menü açık mıydı tanısı için).
                firstScreen ??= lastScreen.Clone();
                var match = TemplateMatcher.FindBest(lastScreen, needle, confidence, out var score);
                if (score > bestSeen)
                {
                    bestSeen = score;
                }
                if (match is not null)
                {
                    return (match, score, null);
                }
                if (sw.ElapsedMilliseconds >= timeoutMs)
                {
                    break;
                }
                await Task.Delay(250);
            }
            while (sw.ElapsedMilliseconds < timeoutMs);

            // Başarısız: node başındaki (first) + en son (screen) kareyi + aranan görüntüyü diske yaz.
            var dumpPath = TryDumpFailure(firstScreen, lastScreen, needle);
            return (null, bestSeen, dumpPath);
        }
        finally
        {
            if (!ReferenceEquals(lastScreen, firstScreen))
            {
                lastScreen?.Dispose();
            }
            firstScreen?.Dispose();
        }
    }

    private string? TryDumpFailure(Mat? firstScreen, Mat? lastScreen, Mat needle)
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "rpa-vision");
            Directory.CreateDirectory(dir);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            if (firstScreen is not null)
            {
                firstScreen.SaveImage(Path.Combine(dir, $"ilk-{stamp}.png"));
            }
            if (lastScreen is not null && !ReferenceEquals(lastScreen, firstScreen))
            {
                lastScreen.SaveImage(Path.Combine(dir, $"son-{stamp}.png"));
            }
            needle.SaveImage(Path.Combine(dir, $"aranan-{stamp}.png"));
            _logger.LogWarning("Vision: görüntü bulunamadı; tanı görüntüleri {Dir} klasörüne yazıldı.", dir);
            return dir;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Vision: tanı görüntüleri yazılamadı.");
            return null;
        }
    }

    private async Task<VisionMatch?> PollForTextAsync(string text, string language, string matchMode, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        do
        {
            using var screen = ScreenCapture.Capture(null, null, null, null);
            var (_, words) = RunOcr(screen, language);
            var hit = words.FirstOrDefault(w => OcrTextMatch.Matches(w.Text, text, matchMode));
            if (hit is not null)
            {
                return hit.Box;
            }
            if (sw.ElapsedMilliseconds >= timeoutMs)
            {
                break;
            }
            await Task.Delay(250);
        }
        while (sw.ElapsedMilliseconds < timeoutMs);
        return null;
    }

    private (string Text, List<OcrWord> Words) RunOcr(Mat image, string language)
    {
        try
        {
            var (full, words) = OcrEngine.Read(image, _tessdataPath, language);
            return (full, words.Select(w => new OcrWord(w.Text, w.Box)).ToList());
        }
        catch (Exception ex) when (ex is not RPA.Domain.Exceptions.SystemException)
        {
            throw new SystemException($"OCR başarısız: {ex.Message}", ex);
        }
    }

    private void DoClick(int x, int y, string? clickType)
    {
        // Eşleşme koordinatları tam-ekran (sanal ekran) yakalamasına göredir; mutlak imleç
        // konumuna çevirmek için sanal ekran orijini eklenir (çoklu monitör; sol/üst negatif olabilir).
        var (ox, oy) = ScreenCapture.VirtualScreenOrigin;
        var absX = x + ox;
        var absY = y + oy;
        System.Windows.Forms.Cursor.Position = new System.Drawing.Point(absX, absY);
        var kind = string.IsNullOrWhiteSpace(clickType) ? "left" : clickType.ToLowerInvariant();
        MouseDownUp(kind == "right" ? RightDown : LeftDown, kind == "right" ? RightUp : LeftUp);
        if (kind == "double")
        {
            MouseDownUp(LeftDown, LeftUp);
        }
        _logger.LogInformation("Vision tıklama: ({X},{Y}) {Kind}", absX, absY, kind);
    }

    private static void MouseDownUp(uint down, uint up)
    {
        mouse_event(down, 0, 0, 0, UIntPtr.Zero);
        mouse_event(up, 0, 0, 0, UIntPtr.Zero);
    }

    private const uint LeftDown = 0x0002, LeftUp = 0x0004, RightDown = 0x0008, RightUp = 0x0010;

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    private sealed record OcrWord(string Text, VisionMatch Box);
}

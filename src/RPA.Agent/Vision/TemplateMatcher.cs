namespace RPA.Agent.Vision;

using System.Runtime.Versioning;
using OpenCvSharp;
using RPA.Domain.ValueObjects;

/// <summary>
/// OpenCvSharp tabanlı template matching. Anchor (Faz 2) için FindAll birden çok eşleşme
/// döndürür; şu an aktiviteler yalnız FindBest'i kullanır. Çok-ölçekli tarama DPI toleransı
/// sağlar.
/// </summary>
[SupportedOSPlatform("windows")]
public static class TemplateMatcher
{
    private static readonly double[] Scales = { 1.0, 0.9, 1.1, 0.8, 1.25 };

    public static VisionMatch? FindBest(Mat haystack, Mat needle, double confidence)
        => FindBest(haystack, needle, confidence, out _);

    /// <summary>
    /// En iyi eşleşmeyi bulur; <paramref name="bestScore"/> eşiğin altında olsa bile ulaşılan en yüksek
    /// normalize skoru döndürür (tanı/confidence ayarı için — "bulunamadı" hatasında gösterilir).
    /// </summary>
    public static VisionMatch? FindBest(Mat haystack, Mat needle, double confidence, out double bestScore)
    {
        bestScore = 0d;
        VisionMatch? best = null;
        foreach (var scale in Scales)
        {
            using var scaled = scale == 1.0
                ? needle.Clone()
                : needle.Resize(default, scale, scale, InterpolationFlags.Area);
            if (scaled.Width > haystack.Width || scaled.Height > haystack.Height)
            {
                continue;
            }

            using var result = new Mat();
            Cv2.MatchTemplate(haystack, scaled, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out Point maxLoc);

            if (maxVal > bestScore)
            {
                bestScore = maxVal;
            }

            if (maxVal >= confidence && (best is null || maxVal > best.Score))
            {
                best = new VisionMatch(maxLoc.X, maxLoc.Y, scaled.Width, scaled.Height, maxVal);
            }
        }
        return best;
    }

    public static IReadOnlyList<VisionMatch> FindAll(Mat haystack, Mat needle, double confidence)
    {
        // Faz 1: tek en iyi eşleşmeyi liste olarak döndür (anchor Faz 2'de genişletilecek).
        var best = FindBest(haystack, needle, confidence);
        return best is null ? Array.Empty<VisionMatch>() : new[] { best };
    }
}

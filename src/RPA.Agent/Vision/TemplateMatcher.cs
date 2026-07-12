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
    {
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
            // SQDIFF (unnormalized) is used instead of the *_NORMED variants because those
            // divide by the template's own norm — a uniform-color template (norm 0, e.g. a
            // solid-color icon) produces a 0/0 degenerate score everywhere. SQDIFF has no such
            // division; we convert it to a [0,1] similarity by scaling against the theoretical
            // worst case (every pixel/channel maximally different).
            Cv2.MatchTemplate(haystack, scaled, result, TemplateMatchModes.SqDiff);
            Cv2.MinMaxLoc(result, out double minVal, out _, out Point minLoc, out _);

            double maxPossibleSqDiff = (double)scaled.Rows * scaled.Cols * scaled.Channels() * 255.0 * 255.0;
            double score = maxPossibleSqDiff > 0 ? 1.0 - (minVal / maxPossibleSqDiff) : 1.0;

            if (score >= confidence && (best is null || score > best.Score))
            {
                best = new VisionMatch(minLoc.X, minLoc.Y, scaled.Width, scaled.Height, score);
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

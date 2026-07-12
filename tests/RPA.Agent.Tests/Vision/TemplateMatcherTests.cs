namespace RPA.Agent.Tests.Vision;

using OpenCvSharp;
using RPA.Agent.Vision;
using Xunit;

public class TemplateMatcherTests
{
    // Dokulu (sıfır olmayan varyanslı) 10x10 needle: siyah zemin + beyaz iç kare.
    // Tekdüze renk DEĞİL — CCoeffNormed'in dejenere olmaması için gerekli.
    private static Mat MakeNeedle()
    {
        var needle = new Mat(new Size(10, 10), MatType.CV_8UC3, Scalar.Black);
        Cv2.Rectangle(needle, new Rect(2, 2, 6, 6), Scalar.White, thickness: -1);
        return needle;
    }

    [Fact]
    public void FindBest_LocatesNeedle_AtKnownPosition()
    {
        var box = new Rect(40, 30, 10, 10);
        using var haystack = new Mat(new Size(100, 100), MatType.CV_8UC3, new Scalar(128, 128, 128));
        using var needle = MakeNeedle();
        // Needle'ı haystack'e piksel-birebir kopyala (bilinen konumda kesin eşleşme).
        using (var roi = new Mat(haystack, box))
        {
            needle.CopyTo(roi);
        }

        var match = TemplateMatcher.FindBest(haystack, needle, confidence: 0.8);

        Assert.NotNull(match);
        Assert.InRange(match!.X, box.X - 2, box.X + 2);
        Assert.InRange(match.Y, box.Y - 2, box.Y + 2);
        Assert.True(match.Score >= 0.8);
    }

    [Fact]
    public void FindBest_ReturnsNull_WhenBelowConfidence()
    {
        // Orta-gri zemin + needle'dan FARKLI iki şekil (renk/boyut farklı) — needle burada yok,
        // ama zemin varyanslı (tekdüze değil) ki CCoeffNormed dejenere olmasın.
        using var haystack = new Mat(new Size(100, 100), MatType.CV_8UC3, new Scalar(128, 128, 128));
        Cv2.Circle(haystack, new Point(20, 20), 8, Scalar.White, thickness: -1);
        Cv2.Rectangle(haystack, new Rect(70, 70, 15, 15), new Scalar(200, 50, 50), thickness: -1);
        using var needle = MakeNeedle(); // haystack içinde yok

        var match = TemplateMatcher.FindBest(haystack, needle, confidence: 0.95);

        Assert.Null(match);
    }
}

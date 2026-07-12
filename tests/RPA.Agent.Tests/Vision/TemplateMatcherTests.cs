namespace RPA.Agent.Tests.Vision;

using OpenCvSharp;
using RPA.Agent.Vision;
using Xunit;

public class TemplateMatcherTests
{
    // 100x100 beyaz zemin, (40,30) konumunda 10x10 siyah kare içeren haystack üret.
    private static Mat MakeHaystack(out Rect knownBox)
    {
        var img = new Mat(new Size(100, 100), MatType.CV_8UC3, Scalar.White);
        knownBox = new Rect(40, 30, 10, 10);
        Cv2.Rectangle(img, knownBox, Scalar.Black, thickness: -1);
        return img;
    }

    private static Mat MakeNeedle()
    {
        // 10x10 siyah kare — haystack'teki desenle aynı.
        return new Mat(new Size(10, 10), MatType.CV_8UC3, Scalar.Black);
    }

    [Fact]
    public void FindBest_LocatesNeedle_AtKnownPosition()
    {
        using var haystack = MakeHaystack(out var box);
        using var needle = MakeNeedle();

        var match = TemplateMatcher.FindBest(haystack, needle, confidence: 0.8);

        Assert.NotNull(match);
        Assert.InRange(match!.X, box.X - 2, box.X + 2);
        Assert.InRange(match.Y, box.Y - 2, box.Y + 2);
        Assert.True(match.Score >= 0.8);
    }

    [Fact]
    public void FindBest_ReturnsNull_WhenBelowConfidence()
    {
        using var haystack = new Mat(new Size(100, 100), MatType.CV_8UC3, Scalar.White);
        using var needle = MakeNeedle(); // siyah kare beyaz zeminde yok

        var match = TemplateMatcher.FindBest(haystack, needle, confidence: 0.95);

        Assert.Null(match);
    }
}

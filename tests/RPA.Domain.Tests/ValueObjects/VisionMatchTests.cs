namespace RPA.Domain.Tests.ValueObjects;

using RPA.Domain.ValueObjects;
using Xunit;

public class VisionMatchTests
{
    [Fact]
    public void Center_IsComputedFromBoundingBox()
    {
        var match = new VisionMatch(X: 100, Y: 200, Width: 40, Height: 20, Score: 0.95);

        Assert.Equal(120, match.CenterX);
        Assert.Equal(210, match.CenterY);
        Assert.Equal(0.95, match.Score);
    }
}

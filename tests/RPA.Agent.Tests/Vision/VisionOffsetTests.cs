namespace RPA.Agent.Tests.Vision;

using RPA.Agent.Vision;
using RPA.Domain.ValueObjects;
using Xunit;

public class VisionOffsetTests
{
    [Fact]
    public void ClickPoint_UsesAnchorBoxCenterPlusOffset()
    {
        // Kutu (x=100,y=200,w=40,h=20) → merkez (120,210); ofset (+50,-10) → (170,200)
        var box = new VisionMatch(100, 200, 40, 20, 1.0);
        var (x, y) = VisionOffset.ClickPoint(box, 50, -10);
        Assert.Equal(170, x);
        Assert.Equal(200, y);
    }

    [Fact]
    public void ClickPoint_ZeroOffset_IsCenter()
    {
        var box = new VisionMatch(0, 0, 10, 10, 1.0);
        var (x, y) = VisionOffset.ClickPoint(box, 0, 0);
        Assert.Equal(5, x);
        Assert.Equal(5, y);
    }
}

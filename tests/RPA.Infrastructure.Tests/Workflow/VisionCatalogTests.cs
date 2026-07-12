namespace RPA.Infrastructure.Tests.Workflow;

using System.Linq;
using RPA.Infrastructure.Workflow;
using Xunit;

public class VisionCatalogTests
{
    [Theory]
    [InlineData("Vision.Click")]
    [InlineData("Vision.WaitFor")]
    [InlineData("Vision.Exists")]
    [InlineData("Vision.GetText")]
    [InlineData("Vision.ClickText")]
    [InlineData("Vision.TextExists")]
    public void Catalog_ContainsVisionActivity(string activityId)
    {
        var catalog = ActivityRegistry.BuildCatalog();
        Assert.Contains(catalog, a => a.Key == activityId);
    }

    [Fact]
    public void VisionActivities_HaveVisionCapability()
    {
        var catalog = ActivityRegistry.BuildCatalog();
        var vision = catalog.Where(a => a.Key.StartsWith("Vision.")).ToList();
        Assert.Equal(6, vision.Count);
        Assert.All(vision, a => Assert.Contains("vision", a.Value.RequiredCapabilities));
    }
}

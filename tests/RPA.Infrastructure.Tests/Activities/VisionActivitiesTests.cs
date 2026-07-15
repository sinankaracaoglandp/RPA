namespace RPA.Infrastructure.Tests.Activities;

using Moq;
using RPA.Domain.Exceptions;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Activities.Vision;
using Xunit;

public class VisionActivitiesTests
{
    private static Mock<IActivityExecutionContext> Ctx(Dictionary<string, object?> vars)
    {
        var ctx = new Mock<IActivityExecutionContext>();
        ctx.Setup(c => c.GetVariable<string?>(It.IsAny<string>()))
           .Returns((string n) => vars.TryGetValue(n, out var v) ? (string?)v : null);
        ctx.Setup(c => c.GetVariable<double>(It.IsAny<string>()))
           .Returns((string n) => vars.TryGetValue(n, out var v) && v is double d ? d : 0d);
        ctx.Setup(c => c.GetVariable<int>(It.IsAny<string>()))
           .Returns((string n) => vars.TryGetValue(n, out var v) && v is int i ? i : 0);
        return ctx;
    }

    [Fact]
    public async Task Click_EmptyImage_ThrowsBusiness()
    {
        var channel = new Mock<IVisionAutomationChannel>();
        var activity = new VisionClickActivity(channel.Object);
        var ctx = Ctx(new() { ["image"] = "" });

        await Assert.ThrowsAsync<BusinessException>(() => activity.ExecuteAsync(ctx.Object));
    }

    [Fact]
    public async Task Click_ValidImage_CallsChannelWithDefaults()
    {
        var channel = new Mock<IVisionAutomationChannel>();
        var activity = new VisionClickActivity(channel.Object);
        var ctx = Ctx(new() { ["image"] = "BASE64", ["confidence"] = 0d, ["timeoutMs"] = 0 });

        await activity.ExecuteAsync(ctx.Object);

        // confidence 0 → varsayılan 0.8, clickType null → "left" kanala bırakılır
        channel.Verify(c => c.ClickImageAsync("BASE64", 0.8, null, 0), Times.Once);
    }

    [Fact]
    public async Task ClickSequence_EmptySteps_ThrowsBusiness()
    {
        var channel = new Mock<IVisionAutomationChannel>();
        var activity = new VisionClickSequenceActivity(channel.Object);
        var ctx = Ctx(new() { ["steps"] = "[]" });

        await Assert.ThrowsAsync<BusinessException>(() => activity.ExecuteAsync(ctx.Object));
    }

    [Fact]
    public async Task ClickSequence_InvalidJson_ThrowsBusiness()
    {
        var channel = new Mock<IVisionAutomationChannel>();
        var activity = new VisionClickSequenceActivity(channel.Object);
        var ctx = Ctx(new() { ["steps"] = "not-json" });

        await Assert.ThrowsAsync<BusinessException>(() => activity.ExecuteAsync(ctx.Object));
    }

    [Fact]
    public async Task ClickSequence_ClicksEachStepInOrder_WithPerStepClickType()
    {
        var calls = new List<(string Image, string? ClickType)>();
        var channel = new Mock<IVisionAutomationChannel>();
        channel.Setup(c => c.ClickImageAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<string?>(), It.IsAny<int>()))
               .Callback((string img, double _, string? ct, int _) => calls.Add((img, ct)))
               .Returns(Task.CompletedTask);
        var activity = new VisionClickSequenceActivity(channel.Object);
        var steps = "[{\"image\":\"A\",\"clickType\":\"left\",\"waitMs\":0}," +
                    "{\"image\":\"B\",\"clickType\":\"double\",\"waitMs\":0}]";
        var ctx = Ctx(new() { ["steps"] = steps, ["confidence"] = 0d, ["timeoutMs"] = 0 });

        await activity.ExecuteAsync(ctx.Object);

        Assert.Equal(new[] { ("A", (string?)"left"), ("B", (string?)"double") }, calls);
        // confidence 0 → 0.8; timeoutMs 0 → 5000 varsayılan
        channel.Verify(c => c.ClickImageAsync("A", 0.8, "left", 5000), Times.Once);
    }

    [Fact]
    public async Task Exists_NotFound_ReturnsFalse_NoThrow()
    {
        var channel = new Mock<IVisionAutomationChannel>();
        channel.Setup(c => c.ImageExistsAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>()))
               .ReturnsAsync(false);
        var activity = new VisionExistsActivity(channel.Object);
        var ctx = Ctx(new() { ["image"] = "BASE64" });

        var result = await activity.ExecuteAsync(ctx.Object);

        Assert.Equal(false, result["exists"]);
    }

    [Fact]
    public async Task GetText_DefaultLanguage_IsTurEng()
    {
        var channel = new Mock<IVisionAutomationChannel>();
        channel.Setup(c => c.GetTextAsync(null, null, null, null, "tur+eng")).ReturnsAsync("okunan");
        var activity = new VisionGetTextActivity(channel.Object);
        var ctx = Ctx(new());

        var result = await activity.ExecuteAsync(ctx.Object);

        Assert.Equal("okunan", result["text"]);
        channel.Verify(c => c.GetTextAsync(null, null, null, null, "tur+eng"), Times.Once);
    }

    [Fact]
    public async Task ClickText_EmptyText_ThrowsBusiness()
    {
        var channel = new Mock<IVisionAutomationChannel>();
        var activity = new VisionClickTextActivity(channel.Object);
        var ctx = Ctx(new() { ["text"] = "  " });

        await Assert.ThrowsAsync<BusinessException>(() => activity.ExecuteAsync(ctx.Object));
    }

    [Fact]
    public async Task ClickTextOffset_EmptyAnchorText_ThrowsBusiness()
    {
        var channel = new Mock<IVisionAutomationChannel>();
        var activity = new VisionClickTextOffsetActivity(channel.Object);
        var ctx = Ctx(new() { ["anchor"] = "{\"anchorText\":\"\",\"dx\":10,\"dy\":0}" });

        await Assert.ThrowsAsync<BusinessException>(() => activity.ExecuteAsync(ctx.Object));
    }

    [Fact]
    public async Task ClickTextOffset_InvalidJson_ThrowsBusiness()
    {
        var channel = new Mock<IVisionAutomationChannel>();
        var activity = new VisionClickTextOffsetActivity(channel.Object);
        var ctx = Ctx(new() { ["anchor"] = "not-json" });

        await Assert.ThrowsAsync<BusinessException>(() => activity.ExecuteAsync(ctx.Object));
    }

    [Fact]
    public async Task ClickTextOffset_Valid_CallsChannelWithParsedValuesAndDefaults()
    {
        var channel = new Mock<IVisionAutomationChannel>();
        var activity = new VisionClickTextOffsetActivity(channel.Object);
        var ctx = Ctx(new()
        {
            ["anchor"] = "{\"anchorText\":\"Malzeme No\",\"dx\":120,\"dy\":-4}",
            ["timeoutMs"] = 0,
        });

        await activity.ExecuteAsync(ctx.Object);

        // language boş → tur+eng; matchMode boş → contains; clickType null → left kanala bırakılır; timeoutMs 0 → 5000
        channel.Verify(c => c.ClickTextOffsetAsync(
            "Malzeme No", 120, -4, "tur+eng", "contains", null, 5000), Times.Once);
    }
}

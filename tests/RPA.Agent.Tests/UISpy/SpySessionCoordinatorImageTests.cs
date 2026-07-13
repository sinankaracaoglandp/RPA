namespace RPA.Agent.Tests.UISpy;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RPA.Agent.UISpy;
using RPA.Infrastructure.UISpy;
using Xunit;

public class SpySessionCoordinatorImageTests
{
    [Fact]
    public async Task Start_ImageKind_SendsImageMessage()
    {
        var sessionId = Guid.NewGuid();
        var transport = new Mock<ISpyElementTransport>();
        var imagePicker = new Mock<IImageRegionPicker>();
        imagePicker.Setup(p => p.DetectOnceAsync(It.IsAny<ImagePickerOptions>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new ImagePick("BASE64", null));
        var options = Options.Create(new SpySessionOptions { TimeoutSeconds = 5 });

        var coordinator = new SpySessionCoordinator(
            Mock.Of<ISapGuiSinglePicker>(), transport.Object, options,
            NullLogger<SpySessionCoordinator>.Instance,
            imagePicker: imagePicker.Object);

        await coordinator.StartAsync(sessionId, "image", "{\"captureMode\":\"timer\",\"delaySeconds\":8}");

        transport.Verify(t => t.SendAsync(
            It.Is<SpyElementMessage>(m => m.Kind == "image" && m.ImageBase64 == "BASE64"),
            It.IsAny<CancellationToken>()), Times.Once);
        // Studio'dan gelen freeze seçenekleri parse edilip picker'a iletilmeli.
        imagePicker.Verify(p => p.DetectOnceAsync(
            It.Is<ImagePickerOptions>(o => o.CaptureMode == "timer" && o.DelaySeconds == 8),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void ImagePickerOptions_Parse_DefaultsToF2()
    {
        var def = ImagePickerOptions.Parse(null);
        Assert.Equal("f2", def.CaptureMode);

        var bad = ImagePickerOptions.Parse("not-json");
        Assert.Equal("f2", bad.CaptureMode);

        var timer = ImagePickerOptions.Parse("{\"captureMode\":\"timer\",\"delaySeconds\":200}");
        Assert.Equal("timer", timer.CaptureMode);
        Assert.Equal(120, timer.DelaySeconds); // clamp 1..120
    }
}

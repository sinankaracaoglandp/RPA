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
        imagePicker.Setup(p => p.DetectOnceAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new ImagePick("BASE64", null));
        var options = Options.Create(new SpySessionOptions { TimeoutSeconds = 5 });

        var coordinator = new SpySessionCoordinator(
            Mock.Of<ISapGuiSinglePicker>(), transport.Object, options,
            NullLogger<SpySessionCoordinator>.Instance,
            imagePicker: imagePicker.Object);

        await coordinator.StartAsync(sessionId, "image");

        transport.Verify(t => t.SendAsync(
            It.Is<SpyElementMessage>(m => m.Kind == "image" && m.ImageBase64 == "BASE64"),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}

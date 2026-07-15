namespace RPA.Agent.Tests.UISpy;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RPA.Agent.UISpy;
using RPA.Infrastructure.UISpy;
using Xunit;

public class SpySessionCoordinatorTextOffsetTests
{
    [Fact]
    public async Task Start_TextOffsetKind_SendsTextOffsetMessage()
    {
        var sessionId = Guid.NewGuid();
        var transport = new Mock<ISpyElementTransport>();
        var picker = new Mock<ITextOffsetPicker>();
        picker.Setup(p => p.DetectOnceAsync(It.IsAny<ImagePickerOptions>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new TextOffsetPick("Malzeme No", 120, -4, "PNG64"));
        var options = Options.Create(new SpySessionOptions { TimeoutSeconds = 5 });

        var coordinator = new SpySessionCoordinator(
            Mock.Of<ISapGuiSinglePicker>(), transport.Object, options,
            NullLogger<SpySessionCoordinator>.Instance,
            textOffsetPicker: picker.Object);

        await coordinator.StartAsync(sessionId, "text-offset", "{\"captureMode\":\"timer\",\"delaySeconds\":8}");

        transport.Verify(t => t.SendAsync(
            It.Is<SpyElementMessage>(m => m.Kind == "text-offset" && m.AnchorText == "Malzeme No" && m.Dx == 120 && m.Dy == -4),
            It.IsAny<CancellationToken>()), Times.Once);
        picker.Verify(p => p.DetectOnceAsync(
            It.Is<ImagePickerOptions>(o => o.CaptureMode == "timer" && o.DelaySeconds == 8),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Start_TextOffsetKind_NoPicker_Throws()
    {
        var coordinator = new SpySessionCoordinator(
            Mock.Of<ISapGuiSinglePicker>(), Mock.Of<ISpyElementTransport>(),
            Options.Create(new SpySessionOptions()), NullLogger<SpySessionCoordinator>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.StartAsync(Guid.NewGuid(), "text-offset"));
    }
}

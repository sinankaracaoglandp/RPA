namespace RPA.Agent.Tests.UISpy;

using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RPA.Agent.UISpy;
using RPA.Domain.ValueObjects;
using RPA.Infrastructure.UISpy;

/// <summary>
/// UI Spy ajan orkestrasyon servisi testleri (Task 4.4, Spec Bölüm 6). Windows P/Invoke ve SAP COM
/// mock'lanır; dedup ve gönderim davranışı doğrulanır.
/// </summary>
public class SapGuiSpyServiceTests
{
    private static (SapGuiSpyService svc, Mock<INativeWindowApi> native, Mock<ISapGuiElementResolver> resolver, Mock<ISpyElementTransport> transport)
        Build()
    {
        var native = new Mock<INativeWindowApi>();
        var resolver = new Mock<ISapGuiElementResolver>();
        var transport = new Mock<ISpyElementTransport>();
        transport.Setup(t => t.SendAsync(It.IsAny<SpyElementMessage>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        var detector = new SapGuiElementDetector(native.Object, resolver.Object, NullLogger<SapGuiElementDetector>.Instance);
        var sender = new SapGuiElementSender(transport.Object, NullLogger<SapGuiElementSender>.Instance);
        var svc = new SapGuiSpyService(detector, sender, NullLogger<SapGuiSpyService>.Instance);
        return (svc, native, resolver, transport);
    }

    private static void PointAtSapElement(Mock<INativeWindowApi> native, Mock<ISapGuiElementResolver> resolver, string id, int x, int y)
    {
        native.Setup(n => n.GetCursorPosition()).Returns((x, y));
        native.Setup(n => n.GetWindowClassAt(x, y)).Returns("SAP_FRONTEND_SESSION");
        resolver.Setup(r => r.ResolveAt(x, y)).Returns(new SapGuiElement(id, "GuiButton", "OK"));
    }

    [Fact]
    public async Task DetectAndSend_WhenSapElement_SendsToStudio()
    {
        var (svc, native, resolver, transport) = Build();
        PointAtSapElement(native, resolver, "wnd[0]/usr/btn[OK]", 10, 20);

        var element = await svc.DetectAndSendAsync();

        Assert.NotNull(element);
        Assert.Equal("wnd[0]/usr/btn[OK]", element!.Id);
        transport.Verify(t => t.SendAsync(It.Is<SpyElementMessage>(m => m.ElementId == "wnd[0]/usr/btn[OK]"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DetectAndSend_SameElementTwice_SendsOnlyOnce()
    {
        var (svc, native, resolver, transport) = Build();
        PointAtSapElement(native, resolver, "wnd[0]/usr/btn[OK]", 10, 20);

        await svc.DetectAndSendAsync();
        await svc.DetectAndSendAsync();

        transport.Verify(t => t.SendAsync(It.IsAny<SpyElementMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DetectAndSend_WhenNotSapWindow_ReturnsNull_AndDoesNotSend()
    {
        var (svc, native, resolver, transport) = Build();
        native.Setup(n => n.GetCursorPosition()).Returns((1, 2));
        native.Setup(n => n.GetWindowClassAt(1, 2)).Returns("Notepad");

        var element = await svc.DetectAndSendAsync();

        Assert.Null(element);
        transport.Verify(t => t.SendAsync(It.IsAny<SpyElementMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DetectAndSend_WhenTransportThrows_DoesNotBubble_AndRetriesNextTime()
    {
        var (svc, native, resolver, transport) = Build();
        PointAtSapElement(native, resolver, "wnd[0]/usr/btn[OK]", 10, 20);
        transport.SetupSequence(t => t.SendAsync(It.IsAny<SpyElementMessage>(), It.IsAny<CancellationToken>()))
                 .ThrowsAsync(new InvalidOperationException("hub down"))
                 .Returns(Task.CompletedTask);

        var first = await svc.DetectAndSendAsync();   // throws internally, swallowed, dedup reset
        var second = await svc.DetectAndSendAsync();   // retried

        Assert.NotNull(first);
        Assert.NotNull(second);
        transport.Verify(t => t.SendAsync(It.IsAny<SpyElementMessage>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public void Constructor_NullArgs_Throw()
    {
        var detector = new SapGuiElementDetector(Mock.Of<INativeWindowApi>(), Mock.Of<ISapGuiElementResolver>(), NullLogger<SapGuiElementDetector>.Instance);
        var sender = new SapGuiElementSender(Mock.Of<ISpyElementTransport>(), NullLogger<SapGuiElementSender>.Instance);
        Assert.Throws<ArgumentNullException>(() => new SapGuiSpyService(null!, sender, NullLogger<SapGuiSpyService>.Instance));
        Assert.Throws<ArgumentNullException>(() => new SapGuiSpyService(detector, null!, NullLogger<SapGuiSpyService>.Instance));
    }
}

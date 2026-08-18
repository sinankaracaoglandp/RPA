namespace RPA.Agent.Tests.UISpy;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RPA.Agent.UISpy;
using RPA.Domain.ValueObjects;
using RPA.Infrastructure.UISpy;

public class SpySessionCoordinatorTests
{
    private static SpySessionCoordinator Build(
        ISapGuiSinglePicker picker,
        ISpyElementTransport transport,
        int timeoutSeconds = 1)
        => new(
            picker,
            transport,
            Options.Create(new SpySessionOptions { TimeoutSeconds = timeoutSeconds }),
            NullLogger<SpySessionCoordinator>.Instance);

    [Fact]
    public async Task StartAsync_Sap_SendsOneElementWithSessionId()
    {
        var sessionId = Guid.NewGuid();
        var picker = new Mock<ISapGuiSinglePicker>();
        var transport = new Mock<ISpyElementTransport>();
        picker.Setup(p => p.DetectOnceAsync(It.IsAny<ImagePickerOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SapGuiElement("wnd[0]/usr/btn[OK]", "GuiButton", "OK"));
        transport.Setup(t => t.SendAsync(It.IsAny<SpyElementMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var coordinator = Build(picker.Object, transport.Object);

        await coordinator.StartAsync(sessionId, "sap");

        transport.Verify(t => t.SendAsync(
            It.Is<SpyElementMessage>(m =>
                m.SessionId == sessionId
                && m.Kind == "sap"
                && m.ElementId == "wnd[0]/usr/btn[OK]"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_Desktop_SendsDesktopMessageWithSelectorAndSessionId()
    {
        var sessionId = Guid.NewGuid();
        var sapPicker = new Mock<ISapGuiSinglePicker>();
        var desktopPicker = new Mock<IDesktopSinglePicker>();
        var transport = new Mock<ISpyElementTransport>();
        desktopPicker.Setup(p => p.DetectOnceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DesktopUiElement("Window/Edit[AutomationId='amount']", "Edit", "Tutar")
            {
                AutomationId = "amount",
                ProcessName = "calc",
            });
        transport.Setup(t => t.SendAsync(It.IsAny<SpyElementMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var coordinator = new SpySessionCoordinator(
            sapPicker.Object, transport.Object,
            Options.Create(new SpySessionOptions { TimeoutSeconds = 1 }),
            NullLogger<SpySessionCoordinator>.Instance,
            desktopPicker.Object);

        await coordinator.StartAsync(sessionId, "desktop");

        transport.Verify(t => t.SendAsync(
            It.Is<SpyElementMessage>(m =>
                m.SessionId == sessionId
                && m.Kind == "desktop"
                && m.ElementId == "Window/Edit[AutomationId='amount']"
                && m.Selector == "Window/Edit[AutomationId='amount']"
                && m.AutomationId == "amount"),
            It.IsAny<CancellationToken>()), Times.Once);
        sapPicker.Verify(p => p.DetectOnceAsync(It.IsAny<ImagePickerOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartAsync_Desktop_WithoutDesktopPicker_Throws()
    {
        var coordinator = Build(Mock.Of<ISapGuiSinglePicker>(), Mock.Of<ISpyElementTransport>());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.StartAsync(Guid.NewGuid(), "desktop"));
    }

    [Fact]
    public async Task StartAsync_WhenNoElement_DoesNotSendAndClearsSession()
    {
        var picker = new Mock<ISapGuiSinglePicker>();
        var transport = new Mock<ISpyElementTransport>();
        picker.Setup(p => p.DetectOnceAsync(It.IsAny<ImagePickerOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SapGuiElement?)null);
        var coordinator = Build(picker.Object, transport.Object);

        await coordinator.StartAsync(Guid.NewGuid(), "sap");
        await coordinator.StartAsync(Guid.NewGuid(), "sap");

        transport.Verify(t => t.SendAsync(It.IsAny<SpyElementMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StopAsync_CancelsActiveSession()
    {
        var sessionId = Guid.NewGuid();
        var picker = new Mock<ISapGuiSinglePicker>();
        var transport = new Mock<ISpyElementTransport>();
        var released = new TaskCompletionSource();
        picker.Setup(p => p.DetectOnceAsync(It.IsAny<ImagePickerOptions>(), It.IsAny<CancellationToken>()))
            .Returns<ImagePickerOptions, CancellationToken>(async (_, ct) =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), ct);
                }
                catch (OperationCanceledException)
                {
                    released.SetResult();
                    throw;
                }

                return null;
            });
        var coordinator = Build(picker.Object, transport.Object, timeoutSeconds: 30);

        var start = coordinator.StartAsync(sessionId, "sap");
        await coordinator.StopAsync(sessionId);

        var completed = await Task.WhenAny(released.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Equal(released.Task, completed);
        await start;
        transport.Verify(t => t.SendAsync(It.IsAny<SpyElementMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartAsync_WhenSessionAlreadyActive_RejectsSecondStart()
    {
        var picker = new Mock<ISapGuiSinglePicker>();
        var transport = new Mock<ISpyElementTransport>();
        picker.Setup(p => p.DetectOnceAsync(It.IsAny<ImagePickerOptions>(), It.IsAny<CancellationToken>()))
            .Returns<ImagePickerOptions, CancellationToken>((_, ct) => Task.Delay(TimeSpan.FromSeconds(30), ct).ContinueWith(_ => (SapGuiElement?)null));
        var coordinator = Build(picker.Object, transport.Object, timeoutSeconds: 30);

        var first = coordinator.StartAsync(Guid.NewGuid(), "sap");

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.StartAsync(Guid.NewGuid(), "sap"));
        await coordinator.StopAsync(Guid.Empty);
        _ = first;
    }

    [Fact]
    public async Task StartAsync_Folder_SendsFolderMessageWithSelectedPath()
    {
        var sessionId = Guid.NewGuid();
        var folderPicker = new Mock<IFolderPicker>();
        var transport = new Mock<ISpyElementTransport>();
        folderPicker.Setup(p => p.DetectOnceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(@"C:\Faturalar\Gelen");
        transport.Setup(t => t.SendAsync(It.IsAny<SpyElementMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var coordinator = new SpySessionCoordinator(
            Mock.Of<ISapGuiSinglePicker>(), transport.Object,
            Options.Create(new SpySessionOptions { TimeoutSeconds = 1 }),
            NullLogger<SpySessionCoordinator>.Instance,
            folderPicker: folderPicker.Object);

        await coordinator.StartAsync(sessionId, "folder");

        transport.Verify(t => t.SendAsync(
            It.Is<SpyElementMessage>(m =>
                m.SessionId == sessionId
                && m.Kind == "folder"
                && m.ElementId == @"C:\Faturalar\Gelen"
                && m.Selector == @"C:\Faturalar\Gelen"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_Folder_WhenCancelled_DoesNotSend()
    {
        var folderPicker = new Mock<IFolderPicker>();
        var transport = new Mock<ISpyElementTransport>();
        folderPicker.Setup(p => p.DetectOnceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        var coordinator = new SpySessionCoordinator(
            Mock.Of<ISapGuiSinglePicker>(), transport.Object,
            Options.Create(new SpySessionOptions { TimeoutSeconds = 1 }),
            NullLogger<SpySessionCoordinator>.Instance,
            folderPicker: folderPicker.Object);

        await coordinator.StartAsync(Guid.NewGuid(), "folder");

        transport.Verify(t => t.SendAsync(It.IsAny<SpyElementMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartAsync_Folder_WithoutFolderPicker_Throws()
    {
        var coordinator = Build(Mock.Of<ISapGuiSinglePicker>(), Mock.Of<ISpyElementTransport>());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.StartAsync(Guid.NewGuid(), "folder"));
    }

    [Fact]
    public async Task StartAsync_UnsupportedKind_Fails()
    {
        var coordinator = Build(Mock.Of<ISapGuiSinglePicker>(), Mock.Of<ISpyElementTransport>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.StartAsync(Guid.NewGuid(), "web"));
    }
}

namespace RPA.Agent.Tests.UISpy;

using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RPA.Agent.UISpy;

public class SpyHubCommandHostedServiceTests
{
    [Fact]
    public async Task StartAsync_RegistersHandlersAndStartsConnection()
    {
        var client = new FakeSpyCommandConnection();
        var coordinator = new Mock<ISpySessionCoordinator>();
        var service = new SpyHubCommandHostedService(
            client,
            coordinator.Object,
            NullLogger<SpyHubCommandHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await client.StartedTask;

        Assert.True(client.Started);
        await client.TriggerStartAsync(Guid.NewGuid(), "sap");
        coordinator.Verify(c => c.StartAsync(It.IsAny<Guid>(), "sap", It.IsAny<CancellationToken>()), Times.Once);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopCommand_StopsCoordinatorSession()
    {
        var client = new FakeSpyCommandConnection();
        var coordinator = new Mock<ISpySessionCoordinator>();
        var service = new SpyHubCommandHostedService(
            client,
            coordinator.Object,
            NullLogger<SpyHubCommandHostedService>.Instance);
        var sessionId = Guid.NewGuid();

        await service.StartAsync(CancellationToken.None);
        await client.StartedTask;
        await client.TriggerStopAsync(sessionId);

        coordinator.Verify(c => c.StopAsync(sessionId, It.IsAny<CancellationToken>()), Times.Once);
        await service.StopAsync(CancellationToken.None);
    }

    private sealed class FakeSpyCommandConnection : ISpyCommandConnection
    {
        private Func<Guid, string, Task>? _startHandler;
        private Func<Guid, Task>? _stopHandler;

        public bool Started { get; private set; }
        public Task StartedTask => _started.Task;
        private readonly TaskCompletionSource _started = new();

        public void OnStartSpy(Func<Guid, string, Task> handler) => _startHandler = handler;
        public void OnStopSpy(Func<Guid, Task> handler) => _stopHandler = handler;
        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            Started = true;
            _started.TrySetResult();
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task TriggerStartAsync(Guid sessionId, string kind) => _startHandler!(sessionId, kind);
        public Task TriggerStopAsync(Guid sessionId) => _stopHandler!(sessionId);
    }
}

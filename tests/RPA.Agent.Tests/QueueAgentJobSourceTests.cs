namespace RPA.Agent.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RPA.Agent.Configuration;
using RPA.Agent.Jobs;
using RPA.Agent.State;
using RPA.Domain.Entities;
using RPA.Domain.Interfaces;

public class QueueAgentJobSourceTests
{
    private static (QueueAgentJobSource src, Mock<IQueueService> queue, AgentState state, Guid queueId) Make()
    {
        var queue = new Mock<IQueueService>();
        var state = new AgentState();
        state.SetRobotId(Guid.NewGuid());
        var queueId = Guid.NewGuid();
        var src = new QueueAgentJobSource(queue.Object, state,
            Options.Create(new AgentOptions { QueueId = queueId }), NullLogger<QueueAgentJobSource>.Instance);
        return (src, queue, state, queueId);
    }

    [Fact]
    public async Task Dequeue_Yapilandirilan_Kuyruk_Ve_RobotId_Ile_Cagirir()
    {
        var (src, queue, state, queueId) = Make();
        var payload = $$"""{ "workflowVersionId": "{{Guid.NewGuid()}}", "arguments": { "a": 1 } }""";
        queue.Setup(q => q.GetNextItemAsync(queueId, state.RobotId!.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueueItem { Id = Guid.NewGuid(), Payload = payload });

        var job = await src.DequeueAsync();

        Assert.NotNull(job);
        Assert.Equal(1L, job!.Arguments["a"]);
        queue.Verify(q => q.GetNextItemAsync(queueId, state.RobotId!.Value, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Kuyruk_Bossa_Null_Doner()
    {
        var (src, queue, _, _) = Make();
        queue.Setup(q => q.GetNextItemAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueueItem?)null);

        Assert.Null(await src.DequeueAsync());
    }

    [Fact]
    public async Task Bozuk_Payload_BusinessException_Olarak_Fail_Edilir_Ve_Null_Doner()
    {
        var (src, queue, _, _) = Make();
        var itemId = Guid.NewGuid();
        queue.Setup(q => q.GetNextItemAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueueItem { Id = itemId, Payload = "{ bozuk" });

        var job = await src.DequeueAsync();

        Assert.Null(job);
        queue.Verify(q => q.FailAsync(itemId, It.IsAny<string>(), true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RobotId_Yoksa_InvalidOperation_Firlatir()
    {
        var queue = new Mock<IQueueService>();
        var src = new QueueAgentJobSource(queue.Object, new AgentState(),
            Options.Create(new AgentOptions()), NullLogger<QueueAgentJobSource>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => src.DequeueAsync());
    }

    [Fact]
    public async Task ReportSuccess_Complete_Cagirir()
    {
        var (src, queue, _, _) = Make();
        var id = Guid.NewGuid();
        await src.ReportSuccessAsync(id);
        queue.Verify(q => q.CompleteAsync(id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReportFailure_Fail_Cagirir()
    {
        var (src, queue, _, _) = Make();
        var id = Guid.NewGuid();
        await src.ReportFailureAsync(id, "hata", isBusinessException: false);
        queue.Verify(q => q.FailAsync(id, "hata", false, It.IsAny<CancellationToken>()), Times.Once);
    }
}

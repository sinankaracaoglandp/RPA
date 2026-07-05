namespace RPA.Agent.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RPA.Agent.Jobs;
using RPA.Agent.State;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Retry;
using BusinessException = RPA.Domain.Exceptions.BusinessException;
using SystemException = RPA.Domain.Exceptions.SystemException;

public class JobExecutorTests
{
    private static AgentJob MakeJob()
        => new(Guid.NewGuid(), new WorkflowVersion { Id = Guid.NewGuid() }, new() { ["x"] = 1 });

    private static JobExecutor Make(Mock<IWorkflowRunner> runner, out AgentState state)
    {
        state = new AgentState();
        return new JobExecutor(runner.Object, new ExceptionClassifier(), state, NullLogger<JobExecutor>.Instance);
    }

    [Fact]
    public async Task Basarili_Calisma_Success_Outcome_Ve_Sayac_Artar()
    {
        var runner = new Mock<IWorkflowRunner>();
        runner.Setup(r => r.ExecuteAsync(It.IsAny<WorkflowVersion>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowExecutionResult { Success = true, Outputs = new() { ["out"] = "ok" } });
        var executor = Make(runner, out var state);
        var job = MakeJob();

        var outcome = await executor.ExecuteAsync(job);

        Assert.True(outcome.Success);
        Assert.Equal("ok", outcome.Outputs["out"]);
        Assert.Equal(1, state.CompletedJobCount);
        Assert.Equal(0, state.FailedJobCount);
        Assert.Null(state.CurrentJobId); // finally temizler
    }

    [Fact]
    public async Task Runner_ExecuteAsync_JobRunId_Olarak_ItemId_Kullanir()
    {
        Guid captured = Guid.Empty;
        var runner = new Mock<IWorkflowRunner>();
        runner.Setup(r => r.ExecuteAsync(It.IsAny<WorkflowVersion>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<WorkflowVersion, Dictionary<string, object?>, Guid, CancellationToken>((_, _, id, _) => captured = id)
            .ReturnsAsync(new WorkflowExecutionResult { Success = true });
        var executor = Make(runner, out _);
        var job = MakeJob();

        await executor.ExecuteAsync(job);

        Assert.Equal(job.ItemId, captured);
    }

    [Fact]
    public async Task BusinessException_Firlatilirsa_Business_Olarak_Siniflanir()
    {
        var runner = new Mock<IWorkflowRunner>();
        runner.Setup(r => r.ExecuteAsync(It.IsAny<WorkflowVersion>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException("Malzeme zaten mevcut"));
        var executor = Make(runner, out var state);

        var outcome = await executor.ExecuteAsync(MakeJob());

        Assert.False(outcome.Success);
        Assert.True(outcome.IsBusinessException);
        Assert.Equal(ExceptionType.Business, outcome.ExceptionType);
        Assert.Equal(1, state.FailedJobCount);
    }

    [Fact]
    public async Task SystemException_Firlatilirsa_System_Olarak_Siniflanir_Ve_Retry_Edilebilir()
    {
        var runner = new Mock<IWorkflowRunner>();
        runner.Setup(r => r.ExecuteAsync(It.IsAny<WorkflowVersion>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SystemException("Bağlantı timeout"));
        var executor = Make(runner, out _);

        var outcome = await executor.ExecuteAsync(MakeJob());

        Assert.False(outcome.Success);
        Assert.False(outcome.IsBusinessException);
        Assert.Equal(ExceptionType.System, outcome.ExceptionType);
    }

    [Fact]
    public async Task Runner_Success_False_Donerse_Istisna_Siniflanir()
    {
        var runner = new Mock<IWorkflowRunner>();
        runner.Setup(r => r.ExecuteAsync(It.IsAny<WorkflowVersion>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowExecutionResult { Success = false, Exception = new BusinessException("kural") });
        var executor = Make(runner, out _);

        var outcome = await executor.ExecuteAsync(MakeJob());

        Assert.False(outcome.Success);
        Assert.True(outcome.IsBusinessException);
    }

    [Fact]
    public async Task Iptal_Edildiginde_OperationCanceled_Yeniden_Firlatir()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var runner = new Mock<IWorkflowRunner>();
        runner.Setup(r => r.ExecuteAsync(It.IsAny<WorkflowVersion>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var executor = Make(runner, out _);

        await Assert.ThrowsAsync<OperationCanceledException>(() => executor.ExecuteAsync(MakeJob(), cts.Token));
    }
}

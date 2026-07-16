namespace RPA.Agent.Tests.Connectivity;

using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RPA.Agent.Connectivity;
using RPA.Agent.Jobs;
using RPA.Agent.State;
using RPA.Domain.Entities;
using RPA.Domain.Exceptions;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Retry;
using Xunit;

/// <summary>
/// Task 6 — bağlantı kirası (15 dk) ve güvenli node sınırı.
/// Zaman daima sahte saatle sürülür; gerçek bekleme YOKTUR.
/// </summary>
public sealed class ConnectivityLeaseTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _now;
        public FakeClock(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    [Fact]
    public void Lease_IsValid_AtFourteenMinutesFiftyNineSeconds()
    {
        var clock = new FakeClock(T0);
        var lease = new ConnectivityLease(clock);
        lease.RecordServerValidation();

        clock.Advance(TimeSpan.FromSeconds(899)); // 14:59

        Assert.True(lease.IsValid);
    }

    [Fact]
    public void Lease_IsExpired_AtExactlyFifteenMinutes()
    {
        var clock = new FakeClock(T0);
        var lease = new ConnectivityLease(clock);
        lease.RecordServerValidation();

        clock.Advance(TimeSpan.FromMinutes(15)); // 15:00

        Assert.False(lease.IsValid);
    }

    [Fact]
    public void RecordServerValidation_RenewsLeaseAndClearsDisconnect()
    {
        var clock = new FakeClock(T0);
        var lease = new ConnectivityLease(clock);
        lease.MarkDisconnected();
        clock.Advance(TimeSpan.FromMinutes(14));

        lease.RecordServerValidation();
        clock.Advance(TimeSpan.FromMinutes(14));

        Assert.True(lease.IsValid);
        Assert.True(lease.IsConnected);
        Assert.Equal(clock.GetUtcNow().AddMinutes(1), lease.ExpiresAt);
    }

    [Fact]
    public void MarkDisconnected_DoesNotImmediatelyInvalidateLease()
    {
        var clock = new FakeClock(T0);
        var lease = new ConnectivityLease(clock);
        lease.RecordServerValidation();

        lease.MarkDisconnected();

        // Bağlantı koptu ≠ kira bitti: çalışan node devam edebilmeli.
        Assert.False(lease.IsConnected);
        Assert.True(lease.IsValid);
    }

    [Fact]
    public async Task Gate_PermitsNextNode_WhileLeaseValid()
    {
        var clock = new FakeClock(T0);
        var lease = new ConnectivityLease(clock);
        lease.RecordServerValidation();
        var gate = new ConnectivityLeaseContinuationGate(lease);

        clock.Advance(TimeSpan.FromSeconds(899));

        await gate.EnsureMayStartNodeAsync(Guid.NewGuid(), "node-2", CancellationToken.None);
    }

    [Fact]
    public async Task Gate_SuspendsNextNode_AfterLeaseExpiry_PreservingJobAndNodeIdentity()
    {
        var clock = new FakeClock(T0);
        var lease = new ConnectivityLease(clock);
        lease.RecordServerValidation();
        var gate = new ConnectivityLeaseContinuationGate(lease);
        var jobRunId = Guid.NewGuid();

        clock.Advance(TimeSpan.FromMinutes(15));

        var ex = await Assert.ThrowsAsync<ExecutionSuspendedException>(
            () => gate.EnsureMayStartNodeAsync(jobRunId, "node-2", CancellationToken.None));

        Assert.Equal(jobRunId, ex.JobRunId);
        Assert.Equal("node-2", ex.NextNodeId);
    }

    [Fact]
    public async Task Gate_DoesNotCancelCurrentNode_OnDisconnect()
    {
        var clock = new FakeClock(T0);
        var lease = new ConnectivityLease(clock);
        lease.RecordServerValidation();
        var gate = new ConnectivityLeaseContinuationGate(lease);
        using var cts = new CancellationTokenSource();

        lease.MarkDisconnected();

        // Kopma iptal üretmez; sınır yalnız sonraki node'da uygulanır.
        Assert.False(cts.IsCancellationRequested);
        await gate.EnsureMayStartNodeAsync(Guid.NewGuid(), "node-2", cts.Token);
    }

    [Fact]
    public async Task JobExecutor_SurfacesSuspension_AsSystemLevelInterruption()
    {
        var job = new AgentJob(Guid.NewGuid(), new WorkflowVersion { Id = Guid.NewGuid() }, new());
        var suspended = new ExecutionSuspendedException(job.ItemId, "node-7", "Kira doldu.");
        var runner = new Mock<IWorkflowRunner>();
        runner.Setup(r => r.ExecuteAsync(
                It.IsAny<WorkflowVersion>(), It.IsAny<Dictionary<string, object?>>(),
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowExecutionResult { Success = false, Exception = suspended });
        var executor = new JobExecutor(
            runner.Object, new ExceptionClassifier(), new AgentState(), NullLogger<JobExecutor>.Instance);

        var outcome = await executor.ExecuteAsync(job);

        Assert.False(outcome.Success);
        Assert.True(outcome.IsSuspended);
        Assert.False(outcome.IsBusinessException);
        var ex = Assert.IsType<ExecutionSuspendedException>(outcome.Exception);
        Assert.Equal("node-7", ex.NextNodeId);
        Assert.Equal(job.ItemId, ex.JobRunId);
    }
}

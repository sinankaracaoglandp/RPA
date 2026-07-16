namespace RPA.Infrastructure.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using RPA.Domain.Entities;
using RPA.Domain.Exceptions;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Workflow;

/// <summary>
/// Task 6 — BaseRunner, her node BAŞLAMADAN önce süreklilik kapısına danışır.
/// Çalışan node iptal edilmez; yalnız sonraki node engellenir.
/// </summary>
public class BaseRunnerConnectivityGateTests
{
    private const string ThreeNodeJson = """
    {
      "schemaVersion": "1.0",
      "id": "22222222-2222-2222-2222-222222222222",
      "name": "Kapı",
      "version": "1.0.0",
      "arguments": { "in": [], "out": [ { "name": "message", "type": "string" } ] },
      "nodes": [
        { "id": "n1", "type": "assign", "variableName": "message", "value": "bir" },
        { "id": "n2", "type": "assign", "variableName": "message", "value": "iki" },
        { "id": "n3", "type": "assign", "variableName": "message", "value": "uc" }
      ],
      "connections": [ { "from": "n1", "to": "n2" }, { "from": "n2", "to": "n3" } ]
    }
    """;

    private sealed class RecordingGate : IExecutionContinuationGate
    {
        private readonly int _allowCount;
        public RecordingGate(int allowCount = int.MaxValue) => _allowCount = allowCount;
        public List<string> Seen { get; } = [];

        public Task EnsureMayStartNodeAsync(Guid jobRunId, string nodeId, CancellationToken ct)
        {
            Seen.Add(nodeId);
            if (Seen.Count > _allowCount)
            {
                throw new ExecutionSuspendedException(jobRunId, nodeId, "Kira doldu.");
            }
            return Task.CompletedTask;
        }
    }

    private static BaseRunner CreateRunner(IExecutionContinuationGate? gate)
        => new(
            new WorkflowValidator(),
            new ActivityCatalog(),
            new EmptyActivityFactory(),
            NullLogger<BaseRunner>.Instance,
            vault: null,
            continuationGate: gate);

    private static WorkflowVersion Version(string json) => new() { JsonDefinition = json };

    [Fact]
    public async Task Runner_ConsultsGate_BeforeEveryNode()
    {
        var gate = new RecordingGate();
        var result = await CreateRunner(gate).ExecuteAsync(Version(ThreeNodeJson), new(), Guid.NewGuid());

        Assert.True(result.Success, result.Exception?.Message);
        Assert.Equal(["n1", "n2", "n3"], gate.Seen);
    }

    [Fact]
    public async Task Runner_SuspendsBeforeNextNode_WhenGateBlocks_PreservingIdentity()
    {
        var gate = new RecordingGate(allowCount: 2); // n1, n2 geçer; n3 engellenir
        var jobRunId = Guid.NewGuid();

        var result = await CreateRunner(gate).ExecuteAsync(Version(ThreeNodeJson), new(), jobRunId);

        Assert.False(result.Success);
        var ex = Assert.IsType<ExecutionSuspendedException>(result.Exception);
        Assert.Equal(jobRunId, ex.JobRunId);
        Assert.Equal("n3", ex.NextNodeId);
        Assert.Equal(["n1", "n2", "n3"], gate.Seen);
    }

    [Fact]
    public async Task Runner_BlockedNodeDoesNotRun_PreviousNodeResultPreserved()
    {
        // n3 engellenirse "message" hâlâ n2'nin değerinde ("iki") kalır — n3 hiç çalışmadı.
        var gate = new RecordingGate(allowCount: 2);
        var result = await CreateRunner(gate).ExecuteAsync(Version(ThreeNodeJson), new(), Guid.NewGuid());

        Assert.False(result.Success);
        Assert.IsType<ExecutionSuspendedException>(result.Exception);
    }

    [Fact]
    public async Task Runner_WithoutGate_RunsUnchanged()
    {
        var result = await CreateRunner(gate: null).ExecuteAsync(Version(ThreeNodeJson), new(), Guid.NewGuid());

        Assert.True(result.Success, result.Exception?.Message);
        Assert.Equal("uc", result.Outputs["message"]);
    }
}

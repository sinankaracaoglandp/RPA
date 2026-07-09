namespace RPA.Infrastructure.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using RPA.Domain.Entities;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Workflow;
using BusinessException = RPA.Domain.Exceptions.BusinessException;
using SystemException = RPA.Domain.Exceptions.SystemException;

/// <summary>
/// BaseRunner state machine testleri (Task 2.2.1). Spec Bölüm 5.2.
/// TDD: doğrusal akış, dallanma, döngü, try/catch, scope izolasyonu, döngü tespiti.
/// </summary>
public class BaseRunnerTests
{
    // ---------------------------------------------------------------- altyapı

    private static BaseRunner CreateRunner(
        IReadOnlyDictionary<string, ActivityMetadata>? catalog = null,
        IActivityFactory? factory = null,
        Func<string, string?, string?>? componentResolver = null)
    {
        var cat = catalog is null ? new ActivityCatalog() : new ActivityCatalog(catalog);
        return new BaseRunner(
            new WorkflowValidator(),
            cat,
            factory ?? new EmptyActivityFactory(),
            NullLogger<BaseRunner>.Instance,
            vault: null,
            componentResolver: componentResolver);
    }

    private static WorkflowVersion Version(string json) => new() { JsonDefinition = json };

    private static ActivityMetadata Meta(string id) =>
        new() { ActivityId = id, DisplayName = id };

    /// <summary>Yapılandırılabilir sahte aktivite.</summary>
    private sealed class FakeActivity : IActivity
    {
        private readonly Func<IActivityExecutionContext, Task<Dictionary<string, object?>>> _fn;
        private readonly ActivityMetadata _meta;

        public FakeActivity(string id, Func<IActivityExecutionContext, Task<Dictionary<string, object?>>> fn)
        {
            _meta = new ActivityMetadata { ActivityId = id, DisplayName = id };
            _fn = fn;
        }

        public Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context) => _fn(context);
        public ActivityMetadata GetMetadata() => _meta;
    }

    private sealed class MapFactory : IActivityFactory
    {
        private readonly Dictionary<string, Func<IActivity>> _map = new();
        public MapFactory Add(string id, Func<IActivity> f) { _map[id] = f; return this; }
        public IActivity? CreateActivity(string activityId) =>
            _map.TryGetValue(activityId, out var f) ? f() : null;
    }

    // ---------------------------------------------------------------- testler

    [Fact]
    public async Task LinearFlow_AssignThenLog_ProducesOutput()
    {
        var json = """
        {
          "schemaVersion": "1.0",
          "id": "11111111-1111-1111-1111-111111111111",
          "name": "Doğrusal",
          "version": "1.0.0",
          "arguments": { "in": [ { "name": "name", "type": "string" } ],
                         "out": [ { "name": "message", "type": "string" } ] },
          "nodes": [
            { "id": "n1", "type": "assign", "variableName": "message", "value": "Merhaba ${name}" },
            { "id": "n2", "type": "log", "message": "log: ${message}" }
          ],
          "connections": [ { "from": "n1", "to": "n2" } ]
        }
        """;

        var runner = CreateRunner();
        var result = await runner.ExecuteAsync(
            Version(json), new() { ["name"] = "Dünya" }, Guid.NewGuid());

        Assert.True(result.Success, result.Exception?.Message);
        Assert.Equal("Merhaba Dünya", result.Outputs["message"]);
    }

    [Fact]
    public async Task If_TrueBranch_Taken()
    {
        var result = await RunIf(nValue: 10);
        Assert.Equal("big", result.Outputs["result"]);
    }

    [Fact]
    public async Task If_FalseBranch_Taken()
    {
        var result = await RunIf(nValue: 3);
        Assert.Equal("small", result.Outputs["result"]);
    }

    private static async Task<WorkflowExecutionResult> RunIf(int nValue)
    {
        var json = """
        {
          "schemaVersion": "1.0",
          "id": "22222222-2222-2222-2222-222222222222",
          "name": "Dallanma",
          "version": "1.0.0",
          "arguments": { "in": [ { "name": "n", "type": "int" } ],
                         "out": [ { "name": "result", "type": "string" } ] },
          "nodes": [
            { "id": "if1", "type": "if", "condition": "${n} > 5" },
            { "id": "big", "type": "assign", "variableName": "result", "value": "big" },
            { "id": "small", "type": "assign", "variableName": "result", "value": "small" }
          ],
          "connections": [
            { "from": "if1", "to": "big", "fromPort": "true" },
            { "from": "if1", "to": "small", "fromPort": "false" }
          ]
        }
        """;
        var runner = CreateRunner();
        var result = await runner.ExecuteAsync(Version(json), new() { ["n"] = nValue }, Guid.NewGuid());
        Assert.True(result.Success, result.Exception?.Message);
        return result;
    }

    [Fact]
    public async Task ForEach_IteratesEachItem()
    {
        var seen = new List<object?>();
        var factory = new MapFactory().Add("Test.Record",
            () => new FakeActivity("Test.Record", ctx =>
            {
                seen.Add(ctx.GetVariable<long>("value"));
                return Task.FromResult(new Dictionary<string, object?>());
            }));

        var json = """
        {
          "schemaVersion": "1.0",
          "id": "33333333-3333-3333-3333-333333333333",
          "name": "Döngü",
          "version": "1.0.0",
          "variables": [ { "name": "nums", "type": "JSON", "default": [1, 2, 3] } ],
          "nodes": [
            { "id": "fe", "type": "forEach", "items": "nums", "itemVariable": "x",
              "bodyStartNodeId": "body", "bodyEndNodeId": "body" },
            { "id": "body", "type": "activity", "activity": "Test.Record",
              "properties": { "value": "${x}" } }
          ],
          "connections": []
        }
        """;

        var runner = CreateRunner(
            catalog: new Dictionary<string, ActivityMetadata> { ["Test.Record"] = Meta("Test.Record") },
            factory: factory);
        var result = await runner.ExecuteAsync(Version(json), new(), Guid.NewGuid());

        Assert.True(result.Success, result.Exception?.Message);
        Assert.Equal(3, seen.Count);
        Assert.Equal(new object?[] { 1L, 2L, 3L }, seen);
    }

    [Fact]
    public async Task While_LoopsUntilConditionFalse()
    {
        var factory = new MapFactory().Add("Test.Countdown",
            () => new FakeActivity("Test.Countdown", ctx =>
            {
                var c = ctx.GetVariable<long>("counter");
                return Task.FromResult(new Dictionary<string, object?> { ["counter"] = c - 1 });
            }));

        var json = """
        {
          "schemaVersion": "1.0",
          "id": "44444444-4444-4444-4444-444444444444",
          "name": "While",
          "version": "1.0.0",
          "variables": [ { "name": "counter", "type": "int", "default": 3 } ],
          "arguments": { "out": [ { "name": "counter", "type": "int" } ] },
          "nodes": [
            { "id": "w", "type": "while", "condition": "${counter} > 0",
              "bodyStartNodeId": "body", "bodyEndNodeId": "body" },
            { "id": "body", "type": "activity", "activity": "Test.Countdown", "properties": {} }
          ],
          "connections": []
        }
        """;

        var runner = CreateRunner(
            catalog: new Dictionary<string, ActivityMetadata> { ["Test.Countdown"] = Meta("Test.Countdown") },
            factory: factory);
        var result = await runner.ExecuteAsync(Version(json), new(), Guid.NewGuid());

        Assert.True(result.Success, result.Exception?.Message);
        Assert.Equal(0L, Convert.ToInt64(result.Outputs["counter"]));
    }

    [Fact]
    public async Task TryCatch_SystemException_RoutesToCatchBranch()
    {
        var factory = new MapFactory().Add("Test.Throw",
            () => new FakeActivity("Test.Throw",
                _ => throw new SystemException("bağlantı koptu")));

        var json = """
        {
          "schemaVersion": "1.0",
          "id": "55555555-5555-5555-5555-555555555555",
          "name": "TryCatch",
          "version": "1.0.0",
          "arguments": { "out": [ { "name": "result", "type": "string" },
                                  { "name": "err", "type": "string" } ] },
          "nodes": [
            { "id": "tc", "type": "tryCatch", "tryNodeId": "boom", "bodyEndNodeId": "boom",
              "catchNodeId": "handle", "exceptionVariable": "err" },
            { "id": "boom", "type": "activity", "activity": "Test.Throw", "properties": {} },
            { "id": "handle", "type": "assign", "variableName": "result", "value": "caught" }
          ],
          "connections": []
        }
        """;

        var runner = CreateRunner(
            catalog: new Dictionary<string, ActivityMetadata> { ["Test.Throw"] = Meta("Test.Throw") },
            factory: factory);
        var result = await runner.ExecuteAsync(Version(json), new(), Guid.NewGuid());

        Assert.True(result.Success, result.Exception?.Message);
        Assert.Equal("caught", result.Outputs["result"]);
        Assert.Equal("bağlantı koptu", result.Outputs["err"]);
    }

    [Fact]
    public async Task TryCatch_FinallyAlwaysRuns()
    {
        var factory = new MapFactory().Add("Test.Throw",
            () => new FakeActivity("Test.Throw", _ => throw new SystemException("hata")));

        var json = """
        {
          "schemaVersion": "1.0",
          "id": "66666666-6666-6666-6666-666666666666",
          "name": "Finally",
          "version": "1.0.0",
          "arguments": { "out": [ { "name": "cleanup", "type": "string" } ] },
          "nodes": [
            { "id": "tc", "type": "tryCatch", "tryNodeId": "boom", "bodyEndNodeId": "boom",
              "catchNodeId": "handle", "finallyNodeId": "fin" },
            { "id": "boom", "type": "activity", "activity": "Test.Throw", "properties": {} },
            { "id": "handle", "type": "log", "message": "yakalandı" },
            { "id": "fin", "type": "assign", "variableName": "cleanup", "value": "done" }
          ],
          "connections": []
        }
        """;

        var runner = CreateRunner(
            catalog: new Dictionary<string, ActivityMetadata> { ["Test.Throw"] = Meta("Test.Throw") },
            factory: factory);
        var result = await runner.ExecuteAsync(Version(json), new(), Guid.NewGuid());

        Assert.True(result.Success, result.Exception?.Message);
        Assert.Equal("done", result.Outputs["cleanup"]);
    }

    [Fact]
    public async Task Activity_OutputStoredAsVariable()
    {
        var factory = new MapFactory().Add("Test.Produce",
            () => new FakeActivity("Test.Produce",
                _ => Task.FromResult(new Dictionary<string, object?> { ["data"] = "42" })));

        var json = """
        {
          "schemaVersion": "1.0",
          "id": "77777777-7777-7777-7777-777777777777",
          "name": "Aktivite",
          "version": "1.0.0",
          "arguments": { "out": [ { "name": "data", "type": "string" } ] },
          "nodes": [ { "id": "a", "type": "activity", "activity": "Test.Produce", "properties": {} } ],
          "connections": []
        }
        """;

        var runner = CreateRunner(
            catalog: new Dictionary<string, ActivityMetadata> { ["Test.Produce"] = Meta("Test.Produce") },
            factory: factory);
        var result = await runner.ExecuteAsync(Version(json), new(), Guid.NewGuid());

        Assert.True(result.Success, result.Exception?.Message);
        Assert.Equal("42", result.Outputs["data"]);
    }

    [Fact]
    public async Task Activity_NotRegistered_FailsWithSystemException()
    {
        var json = """
        {
          "schemaVersion": "1.0",
          "id": "88888888-8888-8888-8888-888888888888",
          "name": "Eksik",
          "version": "1.0.0",
          "nodes": [ { "id": "a", "type": "activity", "activity": "Logic.Log", "properties": {} } ],
          "connections": []
        }
        """;

        var runner = CreateRunner(); // EmptyActivityFactory → null
        var result = await runner.ExecuteAsync(Version(json), new(), Guid.NewGuid());

        Assert.False(result.Success);
        Assert.IsType<SystemException>(result.Exception);
    }

    [Fact]
    public async Task Terminate_BusinessException_Propagates()
    {
        var json = """
        {
          "schemaVersion": "1.0",
          "id": "99999999-9999-9999-9999-999999999999",
          "name": "Terminate",
          "version": "1.0.0",
          "nodes": [ { "id": "t", "type": "terminate", "exceptionType": "Business",
                       "message": "iş kuralı" } ],
          "connections": []
        }
        """;

        var runner = CreateRunner();
        var result = await runner.ExecuteAsync(Version(json), new(), Guid.NewGuid());

        Assert.False(result.Success);
        Assert.IsType<BusinessException>(result.Exception);
        Assert.Equal("iş kuralı", result.Exception!.Message);
    }

    [Fact]
    public async Task CircularGraph_DetectedBeforeExecution()
    {
        var json = """
        {
          "schemaVersion": "1.0",
          "id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
          "name": "Döngüsel",
          "version": "1.0.0",
          "nodes": [
            { "id": "n1", "type": "log", "message": "a" },
            { "id": "n2", "type": "log", "message": "b" }
          ],
          "connections": [
            { "from": "n1", "to": "n2" },
            { "from": "n2", "to": "n1" }
          ]
        }
        """;

        var runner = CreateRunner();
        var result = await runner.ExecuteAsync(Version(json), new(), Guid.NewGuid());

        Assert.False(result.Success);
        Assert.IsType<SystemException>(result.Exception);
        Assert.Contains("döngü", result.Exception!.Message);
    }

    [Fact]
    public async Task InvalidNodeType_FailsSchemaValidation()
    {
        var json = """
        {
          "schemaVersion": "1.0",
          "id": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
          "name": "Geçersiz",
          "version": "1.0.0",
          "nodes": [ { "id": "n1", "type": "bogusType" } ],
          "connections": []
        }
        """;

        var runner = CreateRunner();
        var result = await runner.ExecuteAsync(Version(json), new(), Guid.NewGuid());

        Assert.False(result.Success);
        Assert.IsType<SystemException>(result.Exception);
    }

    [Fact]
    public async Task MalformedJson_FailsBeforeExecution()
    {
        var runner = CreateRunner();
        var result = await runner.ExecuteAsync(Version("{ this is not json"), new(), Guid.NewGuid());

        Assert.False(result.Success);
        Assert.IsType<SystemException>(result.Exception);
    }

    [Fact]
    public async Task Component_InvokedWithIsolatedScope_ReturnsMappedOutput()
    {
        var componentJson = """
        {
          "schemaVersion": "1.0",
          "id": "cccccccc-cccc-cccc-cccc-cccccccccccc",
          "name": "Bileşen",
          "version": "1.0.0",
          "arguments": { "in": [ { "name": "greeting", "type": "string" } ],
                         "out": [ { "name": "shout", "type": "string" } ] },
          "nodes": [ { "id": "n1", "type": "assign", "variableName": "shout", "value": "${greeting}!" } ],
          "connections": []
        }
        """;

        var runner = CreateRunner();
        var outputs = await runner.InvokeComponentAsync(
            new ComponentVersion { JsonDefinition = componentJson },
            new() { ["greeting"] = "selam" },
            Guid.NewGuid());

        Assert.Equal("selam!", outputs["shout"]);
    }

    [Fact]
    public async Task ComponentCall_Node_IsolatesAndMapsVariables()
    {
        var componentJson = """
        {
          "schemaVersion": "1.0",
          "id": "dddddddd-dddd-dddd-dddd-dddddddddddd",
          "name": "AltBileşen",
          "version": "1.0.0",
          "arguments": { "in": [ { "name": "input", "type": "string" } ],
                         "out": [ { "name": "output", "type": "string" } ] },
          "nodes": [ { "id": "n1", "type": "assign", "variableName": "output", "value": "islenmis-${input}" } ],
          "connections": []
        }
        """;

        var json = """
        {
          "schemaVersion": "1.0",
          "id": "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
          "name": "AnaAkış",
          "version": "1.0.0",
          "variables": [ { "name": "source", "type": "string", "default": "veri" } ],
          "arguments": { "out": [ { "name": "final", "type": "string" } ] },
          "nodes": [
            { "id": "cc", "type": "componentCall", "componentId": "dddddddd-dddd-dddd-dddd-dddddddddddd",
              "componentVersion": "1.0.0",
              "inputMapping": { "input": "source" },
              "outputMapping": { "output": "final" } }
          ],
          "connections": []
        }
        """;

        var runner = CreateRunner(componentResolver: (id, ver) => componentJson);
        var result = await runner.ExecuteAsync(Version(json), new(), Guid.NewGuid());

        Assert.True(result.Success, result.Exception?.Message);
        Assert.Equal("islenmis-veri", result.Outputs["final"]);
    }

    [Fact]
    public async Task ActivityProperty_UsesDeclaredVariableDefault_WithMustacheSyntax()
    {
        var json = """
        {
          "schemaVersion": "1.0",
          "id": "f1f1f1f1-f1f1-f1f1-f1f1-f1f1f1f1f1f1",
          "name": "Degiskenli Aktivite",
          "version": "1.0.0",
          "variables": [ { "name": "siteUrl", "type": "string", "default": "example.com" } ],
          "arguments": { "out": [ { "name": "captured", "type": "string" } ] },
          "nodes": [
            { "id": "n1", "type": "activity", "activity": "Test.CaptureUrl",
              "properties": { "url": "{{siteUrl}}" } }
          ],
          "connections": []
        }
        """;

        var factory = new MapFactory().Add(
            "Test.CaptureUrl",
            () => new FakeActivity("Test.CaptureUrl", ctx =>
            {
                var url = ctx.GetVariable<string>("url");
                return Task.FromResult(new Dictionary<string, object?> { ["captured"] = url });
            }));

        var runner = CreateRunner(
            catalog: new Dictionary<string, ActivityMetadata> { ["Test.CaptureUrl"] = Meta("Test.CaptureUrl") },
            factory: factory);

        var result = await runner.ExecuteAsync(Version(json), new(), Guid.NewGuid());

        Assert.True(result.Success, result.Exception?.Message);
        Assert.Equal("example.com", result.Outputs["captured"]);
    }

    // ---------------------------------------------------------------- birim testler

    [Fact]
    public void VariableScope_PushPop_IsolatesInnerVariables()
    {
        var scope = new VariableScope();
        scope.SetGlobalVariable("g", "global");

        scope.PushScope("inner");
        scope.SetLocalVariable("local", "içeride");
        Assert.Equal("içeride", scope.GetVariable<string>("local"));
        Assert.Equal("global", scope.GetVariable<string>("g")); // dış scope okunabilir

        scope.PopScope();
        Assert.False(scope.TryGetVariable("local", out _)); // izole → dışa sızmaz
        Assert.Equal("global", scope.GetVariable<string>("g"));
    }

    [Fact]
    public void VariableScope_MissingVariable_ReturnsDefault()
    {
        var scope = new VariableScope();
        Assert.Null(scope.GetVariable<string>("yok"));
        Assert.Equal(0, scope.GetVariable<int>("yok"));
    }

    [Fact]
    public void ExpressionEvaluator_Comparison_And_Template()
    {
        var scope = new VariableScope();
        scope.SetGlobalVariable("a", 10L);
        scope.SetGlobalVariable("b", 5L);
        scope.SetGlobalVariable("name", "Ada");
        var eval = new ExpressionEvaluator(scope);

        Assert.True(eval.EvaluateCondition("${a} > ${b}"));
        Assert.False(eval.EvaluateCondition("${a} == ${b}"));
        Assert.True(eval.EvaluateCondition("${a} == 10"));
        Assert.Equal("Merhaba Ada", eval.EvaluateString("Merhaba ${name}"));
        Assert.Equal(10L, eval.EvaluateValue("${a}"));
        Assert.True(eval.EvaluateCondition("{{a}} > {{b}}"));
        Assert.Equal("Merhaba Ada", eval.EvaluateString("Merhaba {{name}}"));
        Assert.Equal(10L, eval.EvaluateValue("{{a}}"));
    }

    [Fact]
    public void ExpressionEvaluator_MixedOperators_RespectsPrecedence()
    {
        var scope = new VariableScope();
        scope.SetGlobalVariable("a", 1L);
        scope.SetGlobalVariable("b", 1L);
        var eval = new ExpressionEvaluator(scope);

        // Basic comparison: 1 > 0 should be true
        Assert.True(eval.EvaluateCondition("1 > 0"));

        // Variable comparison: ${b} > 0 should be true
        Assert.True(eval.EvaluateCondition("${b} > 0"));

        // Nested: ${a} == 1 > 0 means ${a} == (1 > 0)
        // Right operand "1 > 0" should be evaluated as condition (true),
        // then compared: 1 == true (converts true to 1) → true
        Assert.True(eval.EvaluateCondition("${a} == 1 > 0"));
    }

    [Fact]
    public async Task ComponentCall_WithUnresolvedId_ErrorMessagePreservesId()
    {
        var json = """
        {
          "schemaVersion": "1.0",
          "id": "ffffffff-ffff-ffff-ffff-ffffffffffff",
          "name": "TestUnresolved",
          "version": "1.0.0",
          "nodes": [
            { "id": "cc", "type": "componentCall", "componentId": "missing-component-id",
              "componentVersion": "1.0.0",
              "inputMapping": {},
              "outputMapping": {} }
          ],
          "connections": []
        }
        """;

        var runner = CreateRunner(componentResolver: (id, ver) => null);
        var result = await runner.ExecuteAsync(Version(json), new(), Guid.NewGuid());

        Assert.False(result.Success);
        Assert.IsType<SystemException>(result.Exception);
        // Error message should show the actual ComponentId
        Assert.Contains("missing-component-id", result.Exception!.Message);
    }

    [Fact]
    public async Task ResumeWithCheckpoint_EmptyOverrides_RestoresState()
    {
        var componentJson = """
        {
          "schemaVersion": "1.0",
          "id": "12121212-1212-1212-1212-121212121212",
          "name": "Stateful",
          "version": "1.0.0",
          "variables": [ { "name": "counter", "type": "int", "default": 0 },
                         { "name": "next", "type": "int", "default": 0 } ],
          "arguments": { "out": [ { "name": "next", "type": "int" } ] },
          "nodes": [
            { "id": "cp", "type": "checkpoint" },
            { "id": "assign_next", "type": "assign", "variableName": "next", "value": "${counter}" }
          ],
          "connections": [ { "from": "cp", "to": "assign_next" } ]
        }
        """;

        var runner = CreateRunner();

        // First execution: reach checkpoint, set counter=5
        var result1 = await runner.ExecuteAsync(Version(componentJson), new() { ["counter"] = 5 }, Guid.NewGuid());
        Assert.True(result1.Success);
        Assert.NotNull(result1.CheckpointData);
        Assert.Equal(5L, Convert.ToInt64(result1.Outputs["next"]));

        // Resume with empty override dict: should restore checkpoint state (counter=5) then continue
        var result2 = await runner.ResumeAsync(
            new WorkflowVersion { JsonDefinition = componentJson },
            new(), // Empty override dict!
            result1.CheckpointData!,
            Guid.NewGuid());

        Assert.True(result2.Success);
        // If checkpoint state was properly imported despite empty dict, counter should still be 5
        Assert.Equal(5L, Convert.ToInt64(result2.Outputs["next"]));
    }
}

namespace RPA.Infrastructure.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Workflow;
using SystemException = RPA.Domain.Exceptions.SystemException;

/// <summary>
/// Component çağrısı testleri (Task 2.4.1). Spec Bölüm 5.4.
/// Kapsam: versiyon pinleme, izole scope, giriş/çıkış eşlemesi, kullanım takibi,
/// ve componentCall node'unun BaseRunner içinde uçtan uca yürütülmesi.
/// </summary>
public class ComponentInvocationTests
{
    private static readonly Guid CompId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    // Girdi 'x'i 'result' çıkışına aktaran basit echo component'i
    // (ifade değerlendirici aritmetik desteklemez; pass-through yeterli).
    private static string DoublerJson(string version) => $$"""
    {
      "schemaVersion": "1.0",
      "id": "{{CompId}}",
      "name": "Echo",
      "version": "{{version}}",
      "arguments": {
        "in": [ { "name": "x", "type": "number" } ],
        "out": [ { "name": "result", "type": "number" } ]
      },
      "nodes": [
        { "id": "n1", "type": "assign", "variableName": "result", "value": "${x}" }
      ],
      "connections": []
    }
    """;

    private static ComponentVersion Version(string version, ComponentStatus status) => new()
    {
        Id = Guid.NewGuid(),
        ComponentId = CompId,
        Version = version,
        Status = status,
        JsonDefinition = DoublerJson(version),
    };

    private static BaseRunner CreateRunner(
        Func<string, string?, string?>? jsonResolver = null)
        => new(
            new WorkflowValidator(),
            new ActivityCatalog(),
            new EmptyActivityFactory(),
            NullLogger<BaseRunner>.Instance,
            vault: null,
            componentResolver: jsonResolver);

    // ---------------------------------------------------------------- SemVer

    [Theory]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("2.0", 2, 0, 0)]
    [InlineData("5", 5, 0, 0)]
    [InlineData("1.4.2-rc1+build9", 1, 4, 2)]
    public void SemanticVersion_Parses(string input, int major, int minor, int patch)
    {
        Assert.True(SemanticVersion.TryParse(input, out var v));
        Assert.Equal(major, v.Major);
        Assert.Equal(minor, v.Minor);
        Assert.Equal(patch, v.Patch);
    }

    [Fact]
    public void SemanticVersion_Compares_ByPrecedence()
    {
        SemanticVersion.TryParse("1.2.0", out var a);
        SemanticVersion.TryParse("1.10.0", out var b);
        Assert.True(b.CompareTo(a) > 0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData(null)]
    public void SemanticVersion_Rejects_Invalid(string? input)
        => Assert.False(SemanticVersion.TryParse(input, out _));

    // ---------------------------------------------------------------- Resolver

    [Fact]
    public void Resolver_PinnedVersion_ReturnsExactMatch()
    {
        var store = new InMemoryComponentStore()
            .Add(Version("1.0.0", ComponentStatus.Published))
            .Add(Version("2.0.0", ComponentStatus.Published));
        var resolver = new ComponentResolver(store);

        var resolved = resolver.Resolve(CompId.ToString(), "1.0.0");

        Assert.NotNull(resolved);
        Assert.Equal("1.0.0", resolved!.Version);
    }

    [Fact]
    public void Resolver_NoVersion_ReturnsLatestPublished()
    {
        var store = new InMemoryComponentStore()
            .Add(Version("1.0.0", ComponentStatus.Published))
            .Add(Version("1.5.0", ComponentStatus.Published))
            .Add(Version("2.0.0", ComponentStatus.Draft)); // draft yayımlanmadı
        var resolver = new ComponentResolver(store);

        var resolved = resolver.Resolve(CompId.ToString(), null);

        Assert.NotNull(resolved);
        Assert.Equal("1.5.0", resolved!.Version); // en yüksek Published
    }

    [Fact]
    public void Resolver_UnknownComponent_ReturnsNull()
    {
        var resolver = new ComponentResolver(new InMemoryComponentStore());
        Assert.Null(resolver.Resolve(Guid.NewGuid().ToString(), null));
    }

    [Fact]
    public void Resolver_PinnedButMissing_ReturnsNull()
    {
        var store = new InMemoryComponentStore().Add(Version("1.0.0", ComponentStatus.Published));
        var resolver = new ComponentResolver(store);
        Assert.Null(resolver.Resolve(CompId.ToString(), "9.9.9"));
    }

    // ---------------------------------------------------------------- UsageTracker

    [Fact]
    public void UsageTracker_Deduplicates_SamePair()
    {
        var tracker = new ComponentUsageTracker();
        var wf = Guid.NewGuid();
        var cv = Version("1.0.0", ComponentStatus.Published);

        tracker.Record(wf, cv);
        tracker.Record(wf, cv);

        Assert.Single(tracker.Usages);
        var usage = tracker.Usages.First();
        Assert.Equal(wf, usage.WorkflowVersionId);
        Assert.Equal(cv.Id, usage.ComponentVersionId);
    }

    [Fact]
    public void UsageTracker_Records_DistinctComponents()
    {
        var tracker = new ComponentUsageTracker();
        var wf = Guid.NewGuid();
        tracker.Record(wf, Version("1.0.0", ComponentStatus.Published));
        tracker.Record(wf, Version("2.0.0", ComponentStatus.Published));
        Assert.Equal(2, tracker.Usages.Count);
    }

    // ---------------------------------------------------------------- ComponentRunner

    [Fact]
    public async Task ComponentRunner_ResolvesRunsAndTracks()
    {
        var store = new InMemoryComponentStore()
            .Add(Version("1.0.0", ComponentStatus.Published))
            .Add(Version("2.0.0", ComponentStatus.Published));
        var tracker = new ComponentUsageTracker();
        var runner = new ComponentRunner(new ComponentResolver(store), CreateRunner(), tracker);
        var wf = Guid.NewGuid();

        var outputs = await runner.InvokeAsync(
            CompId.ToString(), null,
            new Dictionary<string, object?> { ["x"] = 21L },
            Guid.NewGuid(), workflowVersionId: wf);

        Assert.Equal(21L, Convert.ToInt64(outputs["result"]));
        // En son Published (2.0.0) pinlenmeli ve kullanım kaydı düşmeli.
        Assert.Single(tracker.Usages);
        Assert.Equal(wf, tracker.Usages.First().WorkflowVersionId);
    }

    [Fact]
    public async Task ComponentRunner_UnresolvableComponent_Throws()
    {
        var runner = new ComponentRunner(
            new ComponentResolver(new InMemoryComponentStore()), CreateRunner());

        await Assert.ThrowsAsync<SystemException>(() => runner.InvokeAsync(
            Guid.NewGuid().ToString(), null, new(), Guid.NewGuid()));
    }

    [Fact]
    public async Task ComponentRunner_IsolatesScope_NoLeakToCaller()
    {
        // Component 'result' değişkenini set eder; izole scope olduğundan yalnızca
        // eşlenen çıkış döner — çağıran scope'a global sızıntı yoktur.
        var store = new InMemoryComponentStore().Add(Version("1.0.0", ComponentStatus.Published));
        var runner = new ComponentRunner(new ComponentResolver(store), CreateRunner());

        var outputs = await runner.InvokeAsync(
            CompId.ToString(), "1.0.0",
            new Dictionary<string, object?> { ["x"] = 5L },
            Guid.NewGuid());

        // Yalnızca bildirilen çıkış argümanı döner ('x' iç değişkeni sızmaz).
        Assert.True(outputs.ContainsKey("result"));
        Assert.False(outputs.ContainsKey("x"));
    }

    // -------------------------------------------------- componentCall node (BaseRunner)

    [Fact]
    public async Task ComponentCallNode_MapsInputAndOutput_EndToEnd()
    {
        var store = new InMemoryComponentStore().Add(Version("1.0.0", ComponentStatus.Published));
        var resolver = new ComponentResolver(store);

        // BaseRunner componentCall yolunu versiyon-pinlemeli resolver'a köprüle.
        var runner = CreateRunner((id, ver) => resolver.Resolve(id, ver)?.JsonDefinition);

        var workflowJson = $$"""
        {
          "schemaVersion": "1.0",
          "id": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
          "name": "CallerWf",
          "version": "1.0.0",
          "arguments": {
            "in": [ { "name": "seed", "type": "number" } ],
            "out": [ { "name": "final", "type": "number" } ]
          },
          "nodes": [
            {
              "id": "c1", "type": "componentCall",
              "componentId": "{{CompId}}", "componentVersion": "1.0.0",
              "inputMapping": { "x": "seed" },
              "outputMapping": { "result": "final" }
            }
          ],
          "connections": []
        }
        """;

        var result = await runner.ExecuteAsync(
            new WorkflowVersion { JsonDefinition = workflowJson },
            new Dictionary<string, object?> { ["seed"] = 10L },
            Guid.NewGuid());

        Assert.True(result.Success, result.Exception?.Message);
        Assert.Equal(10L, Convert.ToInt64(result.Outputs["final"]));
    }
}

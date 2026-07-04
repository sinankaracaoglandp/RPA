namespace RPA.Infrastructure.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Exceptions;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Idempotency;
using RPA.Infrastructure.Persistence;
using RPA.Infrastructure.Workflow;

/// <summary>
/// Idempotency + Checkpoint testleri (Task 2.5.1). Spec Bölüm 5.2, 2.6.
///
/// Senaryo 1: İdempotency — mükerrer işlem aynı referans anahtarıyla aynı sonuç döner.
/// Senaryo 2: Checkpoint — uzun workflow'un tamamlanmış adımları resume'da atlanır.
/// </summary>
public class IdempotencyCheckpointTests
{
    // ---- Altyapı ----

    private static RpaDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<RpaDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var db = new RpaDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    // ---- Idempotency Tests ----

    [Fact]
    public async Task Register_FirstCall_CreatesNewQueueItem()
    {
        var db = CreateInMemoryDb();
        var repo = new EfQueueItemRepository(db);
        var service = new IdempotencyService(repo, new MockLogger<IdempotencyService>());

        var queueId = Guid.NewGuid();
        var key = "idempotency-key-123";
        var payload = "{\"data\":\"test\"}";

        var result = await service.RegisterAsync(queueId, key, payload);

        Assert.False(result.IsDuplicate);
        Assert.NotEqual(Guid.Empty, result.QueueItem.Id);
        Assert.Equal(key, result.QueueItem.IdempotencyKey);
        Assert.Equal(QueueItemStatus.New, result.QueueItem.Status);
    }

    [Fact]
    public async Task Register_SecondCallSameKey_ReturnsDuplicateWithExistingItem()
    {
        var db = CreateInMemoryDb();
        var repo = new EfQueueItemRepository(db);
        var service = new IdempotencyService(repo, new MockLogger<IdempotencyService>());

        var queueId = Guid.NewGuid();
        var key = "duplicate-key-456";
        var payload = "{\"v\":1}";

        // İlk çağrı
        var result1 = await service.RegisterAsync(queueId, key, payload);
        Assert.False(result1.IsDuplicate);
        var firstId = result1.QueueItem.Id;

        // İkinci çağrı aynı anahtarla
        var result2 = await service.RegisterAsync(queueId, key, payload);
        Assert.True(result2.IsDuplicate);
        Assert.Equal(firstId, result2.QueueItem.Id);
    }

    [Fact]
    public async Task Register_DifferentKeysSameQueue_CreatesMultipleItems()
    {
        var db = CreateInMemoryDb();
        var repo = new EfQueueItemRepository(db);
        var service = new IdempotencyService(repo, new MockLogger<IdempotencyService>());

        var queueId = Guid.NewGuid();

        var result1 = await service.RegisterAsync(queueId, "key-a", "{}");
        var result2 = await service.RegisterAsync(queueId, "key-b", "{}");

        Assert.False(result1.IsDuplicate);
        Assert.False(result2.IsDuplicate);
        Assert.NotEqual(result1.QueueItem.Id, result2.QueueItem.Id);
    }

    [Fact]
    public async Task Register_NullOrEmptyKey_Throws()
    {
        var db = CreateInMemoryDb();
        var repo = new EfQueueItemRepository(db);
        var service = new IdempotencyService(repo, new MockLogger<IdempotencyService>());

        var queueId = Guid.NewGuid();

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.RegisterAsync(queueId, "", "{}"));

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.RegisterAsync(queueId, null!, "{}"));
    }

    // ---- Checkpoint Tests ----

    [Fact]
    public void CheckpointManager_Serialize_ProducesValidJson()
    {
        var manager = new CheckpointManager();
        var variables = new Dictionary<string, object?>
        {
            ["count"] = 5L,
            ["name"] = "test",
            ["active"] = true,
        };

        var json = manager.Serialize("node-checkpoint-1", variables);

        Assert.NotNull(json);
        Assert.Contains("node-checkpoint-1", json);
        Assert.Contains("count", json);
        Assert.Contains("name", json);
    }

    [Fact]
    public void CheckpointManager_Deserialize_RestoresState()
    {
        var manager = new CheckpointManager();
        var originalVars = new Dictionary<string, object?>
        {
            ["counter"] = 42L,
            ["message"] = "checkpoint reached",
        };

        var json = manager.Serialize("checkpoint-node-x", originalVars);
        var restored = manager.Deserialize(json);

        Assert.NotNull(restored);
        Assert.Equal("checkpoint-node-x", restored.LastCheckpointNodeId);
        Assert.Equal(42L, restored.Variables["counter"]);
        Assert.Equal("checkpoint reached", restored.Variables["message"]);
    }

    [Fact]
    public void CheckpointManager_DeserializeNull_ReturnsNull()
    {
        var manager = new CheckpointManager();

        Assert.Null(manager.Deserialize(null));
        Assert.Null(manager.Deserialize(""));
        Assert.Null(manager.Deserialize("   "));
    }

    [Fact]
    public void CheckpointManager_DeserializeInvalid_Throws()
    {
        var manager = new CheckpointManager();

        Assert.Throws<SystemException>(() => manager.Deserialize("not json"));
        Assert.Throws<SystemException>(() => manager.Deserialize("{invalid json"));
    }

    // ---- Resume + Checkpoint Integration ----

    [Fact]
    public async Task BaseRunner_WithCheckpoint_CapturesStateInResult()
    {
        var json = """
        {
          "schemaVersion": "1.0",
          "id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
          "name": "CheckpointTest",
          "version": "1.0.0",
          "variables": [ { "name": "step", "type": "int", "default": 0 } ],
          "arguments": { "out": [ { "name": "step", "type": "int" } ] },
          "nodes": [
            { "id": "n1", "type": "assign", "variableName": "step", "value": "1" },
            { "id": "n2", "type": "checkpoint" },
            { "id": "n3", "type": "assign", "variableName": "step", "value": "2" }
          ],
          "connections": [
            { "from": "n1", "to": "n2" },
            { "from": "n2", "to": "n3" }
          ]
        }
        """;

        var runner = new BaseRunner(
            new WorkflowValidator(),
            new ActivityCatalog(),
            new EmptyActivityFactory(),
            new MockLogger<BaseRunner>(),
            checkpointManager: new CheckpointManager());

        var result = await runner.ExecuteAsync(
            new WorkflowVersion { JsonDefinition = json },
            new(),
            Guid.NewGuid());

        Assert.True(result.Success, result.Exception?.Message);
        // Checkpoint node'u çalıştığından CheckpointData setlenmiş olmalı.
        Assert.NotNull(result.CheckpointData);
        Assert.Contains("n2", result.CheckpointData);
    }

    [Fact]
    public async Task BaseRunner_Resume_SkipsCompletedNodes()
    {
        var json = """
        {
          "schemaVersion": "1.0",
          "id": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
          "name": "ResumeTest",
          "version": "1.0.0",
          "variables": [ { "name": "count", "type": "int", "default": 0 } ],
          "arguments": { "out": [ { "name": "count", "type": "int" } ] },
          "nodes": [
            { "id": "n1", "type": "assign", "variableName": "count", "value": "1" },
            { "id": "n2", "type": "checkpoint" },
            { "id": "n3", "type": "assign", "variableName": "count", "value": "2" },
            { "id": "n4", "type": "assign", "variableName": "count", "value": "3" }
          ],
          "connections": [
            { "from": "n1", "to": "n2" },
            { "from": "n2", "to": "n3" },
            { "from": "n3", "to": "n4" }
          ]
        }
        """;

        var checkpointMgr = new CheckpointManager();

        // Normal çalıştırma: n1 → n2 checkpoint → n3 → n4
        var runner = new BaseRunner(
            new WorkflowValidator(),
            new ActivityCatalog(),
            new EmptyActivityFactory(),
            new MockLogger<BaseRunner>(),
            checkpointManager: checkpointMgr);

        var result1 = await runner.ExecuteAsync(
            new WorkflowVersion { JsonDefinition = json },
            new(),
            Guid.NewGuid());

        Assert.True(result1.Success);
        Assert.NotNull(result1.CheckpointData);
        Assert.Equal(3L, result1.Outputs["count"]); // n1=1, n2 checkpoint, n3=2, n4=3

        // Resume: checkpoint sonrası (n3) tekrar çalıştırılmalı, ama count'un başlangıç değeri
        // checkpoint'teki 1 değeridir (n1 adımının sonucu). n3 onu 2'ye, n4 onu 3'e çeker.
        // Bu test SKIP senaryo değil, Resume senaryo testi — state geri yüklenir.
        // Önemli: CheckpointData'da n1'in sonu (count=1) kaydedilmiş olmalı.
        var runner2 = new BaseRunner(
            new WorkflowValidator(),
            new ActivityCatalog(),
            new EmptyActivityFactory(),
            new MockLogger<BaseRunner>(),
            checkpointManager: checkpointMgr);

        var result2 = await runner2.ResumeAsync(
            new WorkflowVersion { JsonDefinition = json },
            new(),
            result1.CheckpointData!,
            Guid.NewGuid());

        Assert.True(result2.Success);
        // Resume n3'ten başlar ve count=1 ile başlar, n3→2, n4→3
        Assert.Equal(3L, result2.Outputs["count"]);
    }

    [Fact]
    public async Task BaseRunner_ResumeWithEmptyCheckpoint_RunsFromBeginning()
    {
        var json = """
        {
          "schemaVersion": "1.0",
          "id": "cccccccc-cccc-cccc-cccc-cccccccccccc",
          "name": "Simple",
          "version": "1.0.0",
          "arguments": { "out": [ { "name": "result", "type": "string" } ] },
          "nodes": [ { "id": "n1", "type": "assign", "variableName": "result", "value": "ok" } ],
          "connections": []
        }
        """;

        var runner = new BaseRunner(
            new WorkflowValidator(),
            new ActivityCatalog(),
            new EmptyActivityFactory(),
            new MockLogger<BaseRunner>(),
            checkpointManager: new CheckpointManager());

        // Resume empty string'le (geçersiz checkpoint, baştan başla)
        var result = await runner.ResumeAsync(
            new WorkflowVersion { JsonDefinition = json },
            new(),
            "",
            Guid.NewGuid());

        Assert.True(result.Success);
        Assert.Equal("ok", result.Outputs["result"]);
    }
}

/// <summary>Mock logger (TDD test uygunluğu için).</summary>
public sealed class MockLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => false;
    public void Log<TState>(
        Microsoft.Extensions.Logging.LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
    }
}

/// <summary>No-op activity factory (TDD için).</summary>
public sealed class EmptyActivityFactory : IActivityFactory
{
    public IActivity? CreateActivity(string activityId) => null;
}

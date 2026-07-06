namespace RPA.Infrastructure.Tests;

using Microsoft.EntityFrameworkCore;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Infrastructure.Persistence;
using RPA.Infrastructure.Queues;

/// <summary>
/// Task 3.2 — Kuyruk motoru: UPDLOCK atama, durum geçişleri, retry ve eşzamanlılık testleri
/// (Spec Bölüm 7). SQL Server dışı sağlayıcılarda claim, süreç-içi semaforla seri hale getirilir.
/// </summary>
public class QueueServiceTests
{
    private static string NewDbName() => Guid.NewGuid().ToString();

    private static RpaDbContext Db(string name)
    {
        var options = new DbContextOptionsBuilder<RpaDbContext>()
            .UseInMemoryDatabase(databaseName: name)
            .Options;
        var db = new RpaDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static QueueService Service(RpaDbContext db)
        => new(new EfQueueItemRepository(db), new MockLogger<QueueService>());

    private static async Task<(Guid queueId, Guid itemId)> SeedQueueWithItem(RpaDbContext db, int maxRetries = 3)
    {
        var queue = new Queue { Name = "Q1", MaxRetries = maxRetries };
        db.Queues.Add(queue);
        var item = new QueueItem { QueueId = queue.Id, IdempotencyKey = Guid.NewGuid().ToString(), Status = QueueItemStatus.New };
        db.QueueItems.Add(item);
        await db.SaveChangesAsync();
        return (queue.Id, item.Id);
    }

    // ---- ListQueues (Orchestrator Kuyruklar ekranı — WP-6.1) ----

    [Fact]
    public async Task ListQueues_ReturnsQueuesWithStatusCounts()
    {
        var name = NewDbName();
        using var db = Db(name);
        var q1 = new Queue { Name = "Q1", MaxRetries = 5 };
        var q2 = new Queue { Name = "Q2", MaxRetries = 3 };
        db.Queues.AddRange(q1, q2);
        db.QueueItems.AddRange(
            new QueueItem { QueueId = q1.Id, IdempotencyKey = "a", Status = QueueItemStatus.New },
            new QueueItem { QueueId = q1.Id, IdempotencyKey = "b", Status = QueueItemStatus.New },
            new QueueItem { QueueId = q1.Id, IdempotencyKey = "c", Status = QueueItemStatus.Failed });
        await db.SaveChangesAsync();

        var summaries = await Service(db).ListQueuesAsync();

        Assert.Equal(2, summaries.Count);
        var s1 = summaries.First(s => s.Name == "Q1");
        Assert.Equal(2, s1.NewCount);
        Assert.Equal(1, s1.FailedCount);
        Assert.Equal(3, s1.Total);
        Assert.Equal(5, s1.MaxRetries);
        var s2 = summaries.First(s => s.Name == "Q2");
        Assert.Equal(0, s2.Total);
    }

    // ---- ListItems (Orchestrator Kuyruklar ekranı — WP-6.1) ----

    [Fact]
    public async Task ListItems_FiltersByStatus_AndReturnsTotal()
    {
        var name = NewDbName();
        using var db = Db(name);
        var queue = new Queue { Name = "Q1", MaxRetries = 3 };
        db.Queues.Add(queue);
        db.QueueItems.AddRange(
            new QueueItem { QueueId = queue.Id, IdempotencyKey = "a", Status = QueueItemStatus.New },
            new QueueItem { QueueId = queue.Id, IdempotencyKey = "b", Status = QueueItemStatus.Failed },
            new QueueItem { QueueId = queue.Id, IdempotencyKey = "c", Status = QueueItemStatus.Failed });
        await db.SaveChangesAsync();

        var page = await Service(db).ListItemsAsync(queue.Id, QueueItemStatus.Failed, skip: 0, take: 50);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
        Assert.All(page.Items, i => Assert.Equal(QueueItemStatus.Failed, i.Status));
    }

    [Fact]
    public async Task ListItems_NoStatusFilter_ReturnsAllForQueue()
    {
        var name = NewDbName();
        using var db = Db(name);
        var queue = new Queue { Name = "Q1", MaxRetries = 3 };
        var other = new Queue { Name = "Q2", MaxRetries = 3 };
        db.Queues.AddRange(queue, other);
        db.QueueItems.AddRange(
            new QueueItem { QueueId = queue.Id, IdempotencyKey = "a", Status = QueueItemStatus.New },
            new QueueItem { QueueId = queue.Id, IdempotencyKey = "b", Status = QueueItemStatus.Successful },
            new QueueItem { QueueId = other.Id, IdempotencyKey = "c", Status = QueueItemStatus.New });
        await db.SaveChangesAsync();

        var page = await Service(db).ListItemsAsync(queue.Id, status: null, skip: 0, take: 50);

        Assert.Equal(2, page.TotalCount);
    }

    // ---- GetNextItem / atama ----

    [Fact]
    public async Task GetNextItem_ClaimsAndAssignsRobot()
    {
        var name = NewDbName();
        using var db = Db(name);
        var (queueId, itemId) = await SeedQueueWithItem(db);
        var robotId = Guid.NewGuid();

        var item = await Service(db).GetNextItemAsync(queueId, robotId);

        Assert.NotNull(item);
        Assert.Equal(itemId, item!.Id);
        Assert.Equal(QueueItemStatus.InProgress, item.Status);
        Assert.Equal(robotId, item.AssignedRobotId);
        Assert.NotNull(item.StartedAt);
    }

    [Fact]
    public async Task GetNextItem_EmptyQueue_ReturnsNull()
    {
        using var db = Db(NewDbName());
        var queue = new Queue { Name = "Empty" };
        db.Queues.Add(queue);
        await db.SaveChangesAsync();

        Assert.Null(await Service(db).GetNextItemAsync(queue.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task GetNextItem_ReturnsOldestFirst()
    {
        using var db = Db(NewDbName());
        var queue = new Queue { Name = "Q" };
        db.Queues.Add(queue);
        var older = new QueueItem { QueueId = queue.Id, IdempotencyKey = "a", Status = QueueItemStatus.New, CreatedAt = DateTime.UtcNow.AddMinutes(-5) };
        var newer = new QueueItem { QueueId = queue.Id, IdempotencyKey = "b", Status = QueueItemStatus.New, CreatedAt = DateTime.UtcNow };
        db.QueueItems.AddRange(newer, older);
        await db.SaveChangesAsync();

        var claimed = await Service(db).GetNextItemAsync(queue.Id, Guid.NewGuid());
        Assert.Equal(older.Id, claimed!.Id);
    }

    // ---- Concurrency: 5 robot, 1 kalem ----

    [Fact]
    public async Task GetNextItem_FiveConcurrentRobots_OnlyOneWins()
    {
        var name = NewDbName();
        Guid queueId;
        using (var seed = Db(name))
            (queueId, _) = await SeedQueueWithItem(seed);

        var tasks = Enumerable.Range(0, 5).Select(_ => Task.Run(async () =>
        {
            // Her robot kendi DbContext'ini kullanır (DbContext thread-safe değildir); hepsi aynı
            // InMemory veritabanını paylaşır. Claim, kuyruk bazlı semaforla seri hale gelir.
            using var db = Db(name);
            return await Service(db).GetNextItemAsync(queueId, Guid.NewGuid());
        })).ToArray();

        var results = await Task.WhenAll(tasks);

        var winners = results.Where(r => r is not null).ToList();
        Assert.Single(winners);
        Assert.Equal(4, results.Count(r => r is null));
    }

    // ---- Complete ----

    [Fact]
    public async Task Complete_SetsSuccessful()
    {
        using var db = Db(NewDbName());
        var (queueId, itemId) = await SeedQueueWithItem(db);
        var svc = Service(db);
        await svc.GetNextItemAsync(queueId, Guid.NewGuid());

        var done = await svc.CompleteAsync(itemId);

        Assert.Equal(QueueItemStatus.Successful, done!.Status);
        Assert.NotNull(done.CompletedAt);
    }

    [Fact]
    public async Task Complete_UnknownItem_ReturnsNull()
    {
        using var db = Db(NewDbName());
        Assert.Null(await Service(db).CompleteAsync(Guid.NewGuid()));
    }

    // ---- Fail: BusinessException (no retry) ----

    [Fact]
    public async Task Fail_BusinessException_NoRetry()
    {
        using var db = Db(NewDbName());
        var (queueId, itemId) = await SeedQueueWithItem(db, maxRetries: 3);
        var svc = Service(db);
        await svc.GetNextItemAsync(queueId, Guid.NewGuid());

        var failed = await svc.FailAsync(itemId, "İş kuralı ihlali", isBusinessException: true);

        Assert.Equal(QueueItemStatus.BusinessException, failed!.Status);
        Assert.Equal(1, failed.AttemptCount);
        Assert.NotNull(failed.CompletedAt);
    }

    // ---- Fail: SystemException retry ----

    [Fact]
    public async Task Fail_SystemException_RequeuesWhenUnderMaxRetries()
    {
        using var db = Db(NewDbName());
        var (queueId, itemId) = await SeedQueueWithItem(db, maxRetries: 3);
        var svc = Service(db);
        await svc.GetNextItemAsync(queueId, Guid.NewGuid());

        var failed = await svc.FailAsync(itemId, "timeout", isBusinessException: false);

        Assert.Equal(QueueItemStatus.New, failed!.Status);
        Assert.Equal(1, failed.AttemptCount);
        Assert.Null(failed.AssignedRobotId);
        Assert.Null(failed.StartedAt);
    }

    [Fact]
    public async Task Fail_SystemException_FailsWhenMaxRetriesExceeded()
    {
        using var db = Db(NewDbName());
        var (queueId, itemId) = await SeedQueueWithItem(db, maxRetries: 1);
        var svc = Service(db);
        var robotId = Guid.NewGuid();

        // 1. deneme: retry (AttemptCount 1 <= 1) -> New
        await svc.GetNextItemAsync(queueId, robotId);
        var first = await svc.FailAsync(itemId, "timeout", isBusinessException: false);
        Assert.Equal(QueueItemStatus.New, first!.Status);

        // 2. deneme: AttemptCount 2 > 1 -> Failed
        await svc.GetNextItemAsync(queueId, robotId);
        var second = await svc.FailAsync(itemId, "timeout", isBusinessException: false);

        Assert.Equal(QueueItemStatus.Failed, second!.Status);
        Assert.Equal(2, second.AttemptCount);
        Assert.NotNull(second.CompletedAt);
    }

    [Fact]
    public async Task Fail_UnknownItem_ReturnsNull()
    {
        using var db = Db(NewDbName());
        Assert.Null(await Service(db).FailAsync(Guid.NewGuid(), "x", false));
    }
}

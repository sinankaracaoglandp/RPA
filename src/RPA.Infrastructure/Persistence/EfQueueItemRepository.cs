namespace RPA.Infrastructure.Persistence;

using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;

/// <summary>EF Core tabanlı <see cref="IQueueItemRepository"/> implementasyonu (Task 2.5.1, 3.2).</summary>
public sealed class EfQueueItemRepository : IQueueItemRepository
{
    private readonly RpaDbContext _db;

    // SQL Server dışı sağlayıcılar (InMemory/Sqlite testleri) satır kilidi desteklemediğinden,
    // claim işlemleri kuyruk bazında bu semaforlarla süreç-içi seri hale getirilir. Böylece
    // eşzamanlılık davranışı (yalnızca bir çağrı kalemi alır) sağlayıcıdan bağımsız test edilebilir.
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> ClaimLocks = new();

    public EfQueueItemRepository(RpaDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public Task<QueueItem?> FindByIdempotencyKeyAsync(
        Guid queueId, string idempotencyKey, CancellationToken cancellationToken = default)
        => _db.QueueItems
            .Where(qi => !qi.IsDeleted && qi.QueueId == queueId && qi.IdempotencyKey == idempotencyKey)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<QueueItem?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _db.QueueItems.FirstOrDefaultAsync(qi => qi.Id == id, cancellationToken);

    public Task<Queue?> FindQueueAsync(Guid queueId, CancellationToken cancellationToken = default)
        => _db.Queues.FirstOrDefaultAsync(q => q.Id == queueId && !q.IsDeleted, cancellationToken);

    public async Task<QueueItem?> ClaimNextNewItemAsync(Guid queueId, Guid robotId, CancellationToken cancellationToken = default)
    {
        if (_db.Database.IsSqlServer())
            return await ClaimWithUpdLockAsync(queueId, robotId, cancellationToken);

        // Sağlayıcı satır kilidi desteklemiyor (test): kuyruk bazında süreç-içi seri hale getir.
        var gate = ClaimLocks.GetOrAdd(queueId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var item = await _db.QueueItems
                .Where(qi => !qi.IsDeleted && qi.QueueId == queueId && qi.Status == QueueItemStatus.New)
                .OrderBy(qi => qi.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (item is null)
                return null;

            Assign(item, robotId);
            await _db.SaveChangesAsync(cancellationToken);
            return item;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<QueueItem?> ClaimWithUpdLockAsync(Guid queueId, Guid robotId, CancellationToken cancellationToken)
    {
        // UPDLOCK: sıradaki satırı yazma amaçlı kilitle. READPAST: başka robotun kilitlediği satırı
        // atla (bekleme). ROWLOCK: satır seviyesinde kilit. Böylece 5 robot aynı anda çağırsa bile
        // her biri farklı (veya boş) satır alır; aynı kalem iki kez atanmaz.
        var status = (int)QueueItemStatus.New;
        var item = await _db.QueueItems
            .FromSqlInterpolated($@"
                SELECT TOP(1) * FROM [QueueItems] WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE [QueueId] = {queueId} AND [Status] = {status} AND [IsDeleted] = 0
                ORDER BY [CreatedAt]")
            .FirstOrDefaultAsync(cancellationToken);
        if (item is null)
            return null;

        Assign(item, robotId);
        await _db.SaveChangesAsync(cancellationToken);
        return item;
    }

    private static void Assign(QueueItem item, Guid robotId)
    {
        item.Status = QueueItemStatus.InProgress;
        item.AssignedRobotId = robotId;
        item.StartedAt = DateTime.UtcNow;
    }

    public async Task AddAsync(QueueItem item, CancellationToken cancellationToken = default)
        => await _db.QueueItems.AddAsync(item, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _db.SaveChangesAsync(cancellationToken);
}

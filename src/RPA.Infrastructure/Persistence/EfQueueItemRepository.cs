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

    public async Task<IReadOnlyList<QueueSummary>> ListQueueSummariesAsync(CancellationToken cancellationToken = default)
    {
        var queues = await _db.Queues.AsNoTracking()
            .Where(q => !q.IsDeleted)
            .OrderBy(q => q.Name)
            .Select(q => new { q.Id, q.Name, q.MaxRetries, q.SlaSeconds })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Durum bazlı sayımlar tek sorguda (queueId + status gruplandırma).
        var counts = await _db.QueueItems.AsNoTracking()
            .Where(qi => !qi.IsDeleted)
            .GroupBy(qi => new { qi.QueueId, qi.Status })
            .Select(g => new { g.Key.QueueId, g.Key.Status, Count = g.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return queues.Select(q =>
        {
            int CountFor(QueueItemStatus s) =>
                counts.FirstOrDefault(c => c.QueueId == q.Id && c.Status == s)?.Count ?? 0;

            var total = counts.Where(c => c.QueueId == q.Id).Sum(c => c.Count);
            return new QueueSummary(
                q.Id, q.Name, q.MaxRetries, q.SlaSeconds,
                CountFor(QueueItemStatus.New),
                CountFor(QueueItemStatus.InProgress),
                CountFor(QueueItemStatus.Failed),
                total);
        }).ToList();
    }

    public async Task<QueueItemPage> ListItemsAsync(
        Guid queueId, QueueItemStatus? status, int skip, int take, CancellationToken cancellationToken = default)
    {
        var q = _db.QueueItems.AsNoTracking().Where(qi => !qi.IsDeleted && qi.QueueId == queueId);

        if (status is { } s)
        {
            q = q.Where(qi => qi.Status == s);
        }

        var total = await q.CountAsync(cancellationToken).ConfigureAwait(false);

        take = take <= 0 ? 50 : take;
        skip = skip < 0 ? 0 : skip;

        var items = await q
            .OrderByDescending(qi => qi.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new QueueItemPage(items, total);
    }

    public Task<Queue?> FindQueueAsync(Guid queueId, CancellationToken cancellationToken = default)
        => _db.Queues.FirstOrDefaultAsync(q => q.Id == queueId && !q.IsDeleted, cancellationToken);

    public async Task<QueueItem?> ClaimNextNewItemAsync(Guid queueId, Guid robotId, CancellationToken cancellationToken = default)
    {
        if (_db.Database.IsNpgsql())
            return await ClaimWithSkipLockedAsync(queueId, robotId, cancellationToken);

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

    private async Task<QueueItem?> ClaimWithSkipLockedAsync(Guid queueId, Guid robotId, CancellationToken cancellationToken)
    {
        // FOR UPDATE: sıradaki satırı yazma amaçlı kilitle. SKIP LOCKED: başka robotun kilitlediği
        // satırı atla (bekleme). Böylece N robot aynı anda çağırsa bile her biri farklı (veya boş)
        // satır alır; aynı kalem iki kez atanmaz. (SQL Server UPDLOCK+READPAST'ın PostgreSQL karşılığı.)
        var status = (int)QueueItemStatus.New;
        var item = await _db.QueueItems
            .FromSqlInterpolated($@"
                SELECT * FROM ""QueueItems""
                WHERE ""QueueId"" = {queueId} AND ""Status"" = {status} AND ""IsDeleted"" = false
                ORDER BY ""CreatedAt""
                LIMIT 1
                FOR UPDATE SKIP LOCKED")
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

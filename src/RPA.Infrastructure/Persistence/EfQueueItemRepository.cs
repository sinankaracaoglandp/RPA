namespace RPA.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using RPA.Domain.Entities;
using RPA.Domain.Interfaces;

/// <summary>EF Core tabanlı <see cref="IQueueItemRepository"/> implementasyonu (Task 2.5.1).</summary>
public sealed class EfQueueItemRepository : IQueueItemRepository
{
    private readonly RpaDbContext _db;

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

    public async Task AddAsync(QueueItem item, CancellationToken cancellationToken = default)
        => await _db.QueueItems.AddAsync(item, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _db.SaveChangesAsync(cancellationToken);
}

namespace RPA.Domain.Interfaces;

using RPA.Domain.Entities;

/// <summary>
/// QueueItem kalıcılık soyutlaması. Idempotency kontrolü referans anahtarı (IdempotencyKey)
/// üzerinden yapılır — Spec Bölüm 4 (QueueItem), 5.2 (Idempotency/Checkpoint).
/// </summary>
public interface IQueueItemRepository
{
    /// <summary>
    /// Aynı kuyrukta aynı referans anahtarına sahip (silinmemiş) QueueItem'ı arar.
    /// Bulunursa mükerrer işlem tespit edilmiş olur.
    /// </summary>
    Task<QueueItem?> FindByIdempotencyKeyAsync(
        Guid queueId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<QueueItem?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddAsync(QueueItem item, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

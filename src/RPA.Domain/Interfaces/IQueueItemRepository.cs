namespace RPA.Domain.Interfaces;

using RPA.Domain.Entities;
using RPA.Domain.Enums;

/// <summary>Sayfalanmış QueueItem sonucu (toplam eşleşen + geçerli sayfa).</summary>
public sealed record QueueItemPage(IReadOnlyList<QueueItem> Items, int TotalCount);

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

    /// <summary>
    /// Orchestrator Kuyruklar ekranı (WP-6.1): bir kuyruğun kalemlerini opsiyonel durum
    /// filtresiyle sayfalı döner (en yeni önce). Salt okuma.
    /// </summary>
    Task<QueueItemPage> ListItemsAsync(
        Guid queueId, QueueItemStatus? status, int skip, int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// Kuyruktan sıradaki (en eski) New kalemi atomik olarak kilitler (SQL Server UPDLOCK/READPAST),
    /// InProgress'e çeker, verilen robota atar, StartedAt'i set eder ve kalıcı hale getirir.
    /// Uygun kalem yoksa null döner. Eşzamanlı çağrılarda yalnızca bir çağrı kalemi alır.
    /// </summary>
    Task<QueueItem?> ClaimNextNewItemAsync(Guid queueId, Guid robotId, CancellationToken cancellationToken = default);

    /// <summary>Kuyruğu Id ile bulur (retry politikası için MaxRetries); yoksa null.</summary>
    Task<Queue?> FindQueueAsync(Guid queueId, CancellationToken cancellationToken = default);

    Task AddAsync(QueueItem item, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

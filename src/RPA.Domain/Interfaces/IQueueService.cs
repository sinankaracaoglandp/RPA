namespace RPA.Domain.Interfaces;

using RPA.Domain.Entities;
using RPA.Domain.Enums;

/// <summary>
/// Kuyruk motoru (Task 3.2, Spec Bölüm 7 — Queue &amp; Dispatch). Robotların iş kalemlerini
/// eşzamanlılık güvenli biçimde (SQL Server UPDLOCK) çekmesini ve durum geçişlerini yönetir.
/// Correlation ID = işlenen QueueItem/JobRun kimliği.
/// </summary>
public interface IQueueService
{
    /// <summary>
    /// Kuyruktan sıradaki (en eski) <see cref="Enums.QueueItemStatus.New"/> kalemi atomik olarak
    /// kilitleyip InProgress'e çeker ve verilen robota atar. Uygun kalem yoksa null döner.
    /// SQL Server üzerinde <c>WITH (UPDLOCK, READPAST, ROWLOCK)</c> ile yarış koşulları engellenir:
    /// aynı anda birden çok robot çağırırsa yalnızca biri kalemi alır.
    /// </summary>
    Task<QueueItem?> GetNextItemAsync(Guid queueId, Guid robotId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Orchestrator Kuyruklar ekranı (WP-6.1): kuyruk kalemlerini opsiyonel durum filtresiyle
    /// sayfalı listeler (en yeni önce). Salt okuma; atama/geçiş yapmaz.
    /// </summary>
    Task<QueueItemPage> ListItemsAsync(
        Guid queueId, QueueItemStatus? status, int skip, int take, CancellationToken cancellationToken = default);

    /// <summary>Tüm kuyrukları durum bazlı kalem sayaçlarıyla listeler (Orchestrator Kuyruklar ekranı).</summary>
    Task<IReadOnlyList<QueueSummary>> ListQueuesAsync(CancellationToken cancellationToken = default);

    /// <summary>Kalemi başarıyla tamamlandı (Successful) olarak işaretler. Bulunamazsa null.</summary>
    Task<QueueItem?> CompleteAsync(Guid itemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Kalemi başarısız işaretler. İş kuralı istisnası (isBusinessException=true) yeniden denenmez
    /// (Status = BusinessException). Sistem istisnası yeniden denenebilir: AttemptCount artırılır ve
    /// kuyruğun MaxRetries değerine ulaşılmadıysa kalem tekrar New'e çekilir; aşılırsa Failed olur.
    /// </summary>
    Task<QueueItem?> FailAsync(Guid itemId, string? errorDetail, bool isBusinessException, CancellationToken cancellationToken = default);
}

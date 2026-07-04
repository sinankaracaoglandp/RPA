namespace RPA.Domain.Interfaces;

using RPA.Domain.Entities;

/// <summary>
/// QueueItem referans anahtarı (idempotency key) ile mükerrer işlem engeli.
/// Spec Bölüm 5.2: "QueueItem referans anahtarı ile mükerrer işlem engellenir."
/// </summary>
public interface IIdempotencyService
{
    /// <summary>
    /// Verilen kuyruk + referans anahtarı için kayıt arar; yoksa yeni bir QueueItem
    /// (Status = New) oluşturup kalıcı hale getirir. Aynı anahtarla ikinci çağrı
    /// yeni bir işlem başlatmaz — var olan kaydı (ve sonucunu) döner.
    /// </summary>
    Task<IdempotencyCheckResult> RegisterAsync(
        Guid queueId,
        string idempotencyKey,
        string payload,
        CancellationToken cancellationToken = default);
}

/// <summary>Idempotency kontrol sonucu.</summary>
public sealed class IdempotencyCheckResult
{
    public IdempotencyCheckResult(bool isDuplicate, QueueItem queueItem)
    {
        IsDuplicate = isDuplicate;
        QueueItem = queueItem;
    }

    /// <summary>true ise bu referans anahtarıyla daha önce bir QueueItem kaydı oluşturulmuş demektir;
    /// çağıran taraf yeni bir işlem başlatmamalı, <see cref="QueueItem"/>'ın mevcut durumunu/sonucunu kullanmalıdır.</summary>
    public bool IsDuplicate { get; }

    /// <summary>Mükerrerse mevcut kayıt; değilse yeni oluşturulan kayıt.</summary>
    public QueueItem QueueItem { get; }
}

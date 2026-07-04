namespace RPA.Infrastructure.Idempotency;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;

/// <summary>
/// QueueItem referans anahtarı (idempotency key) ile mükerrer işlem engeli (Task 2.5.1).
/// Spec Bölüm 5.2: "QueueItem referans anahtarı ile mükerrer işlem engellenir."
/// </summary>
public sealed class IdempotencyService : IIdempotencyService
{
    private readonly IQueueItemRepository _repository;
    private readonly ILogger<IdempotencyService> _logger;

    public IdempotencyService(IQueueItemRepository repository, ILogger<IdempotencyService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IdempotencyCheckResult> RegisterAsync(
        Guid queueId, string idempotencyKey, string payload, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("Referans anahtarı (idempotencyKey) boş olamaz.", nameof(idempotencyKey));
        }

        var existing = await _repository.FindByIdempotencyKeyAsync(queueId, idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Mükerrer işlem tespit edildi: Queue {QueueId}, ReferansAnahtarı {Key} — mevcut QueueItem {ItemId} (Durum: {Status}) döndürülüyor.",
                queueId, idempotencyKey, existing.Id, existing.Status);
            return new IdempotencyCheckResult(isDuplicate: true, existing);
        }

        var item = new QueueItem
        {
            QueueId = queueId,
            IdempotencyKey = idempotencyKey,
            Payload = payload,
            Status = QueueItemStatus.New,
        };

        try
        {
            await _repository.AddAsync(item, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Eşzamanlı iki çağrı aynı anahtarla yarıştıysa: benzersiz kısıt (QueueId, IdempotencyKey)
            // ihlali oluşur — bu durumda kazanan kaydı çekip mükerrer olarak dön.
            var raced = await _repository.FindByIdempotencyKeyAsync(queueId, idempotencyKey, cancellationToken);
            if (raced is not null)
            {
                _logger.LogWarning(
                    "Eşzamanlı yarış tespit edildi: Queue {QueueId}, ReferansAnahtarı {Key} — mevcut kayıt kullanılıyor.",
                    queueId, idempotencyKey);
                return new IdempotencyCheckResult(isDuplicate: true, raced);
            }
            throw;
        }

        _logger.LogInformation(
            "Yeni QueueItem oluşturuldu: Queue {QueueId}, ReferansAnahtarı {Key}, ItemId {ItemId}.",
            queueId, idempotencyKey, item.Id);
        return new IdempotencyCheckResult(isDuplicate: false, item);
    }
}

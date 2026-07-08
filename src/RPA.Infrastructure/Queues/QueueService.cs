namespace RPA.Infrastructure.Queues;

using Microsoft.Extensions.Logging;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;

/// <summary>
/// Kuyruk motoru (Task 3.2, Spec Bölüm 7). İş kalemlerini UPDLOCK ile atomik çeker, durum
/// geçişlerini ve yeniden deneme politikasını uygular. Kuyruk çekimi ve robot ataması loglanır.
/// </summary>
public sealed class QueueService : IQueueService
{
    private readonly IQueueItemRepository _repository;
    private readonly ILogger<QueueService> _logger;

    public QueueService(IQueueItemRepository repository, ILogger<QueueService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<QueueItemPage> ListItemsAsync(
        Guid queueId, QueueItemStatus? status, int skip, int take, CancellationToken cancellationToken = default)
        => _repository.ListItemsAsync(queueId, status, skip, take, cancellationToken);

    public Task<QueueItem?> GetItemAsync(Guid itemId, CancellationToken cancellationToken = default)
        => _repository.FindByIdAsync(itemId, cancellationToken);

    public Task<IReadOnlyList<QueueSummary>> ListQueuesAsync(CancellationToken cancellationToken = default)
        => _repository.ListQueueSummariesAsync(cancellationToken);

    public async Task<QueueItem?> GetNextItemAsync(Guid queueId, Guid robotId, CancellationToken cancellationToken = default)
    {
        var item = await _repository.ClaimNextNewItemAsync(queueId, robotId, cancellationToken);
        if (item is null)
        {
            _logger.LogDebug("Kuyruk {QueueId} boş — robot {RobotId} için işlenecek kalem yok.", queueId, robotId);
            return null;
        }

        _logger.LogInformation("QueueItem {ItemId} kuyruk {QueueId} robot {RobotId} tarafından alındı (InProgress).",
            item.Id, queueId, robotId);
        return item;
    }

    public async Task<QueueItem?> CompleteAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        var item = await _repository.FindByIdAsync(itemId, cancellationToken);
        if (item is null)
        {
            _logger.LogWarning("Complete: bilinmeyen QueueItem {ItemId}.", itemId);
            return null;
        }

        item.Status = QueueItemStatus.Successful;
        item.CompletedAt = DateTime.UtcNow;
        item.ErrorDetail = null;
        await _repository.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("QueueItem {ItemId} tamamlandı (Successful).", item.Id);
        return item;
    }

    public async Task<QueueItem?> FailAsync(Guid itemId, string? errorDetail, bool isBusinessException, CancellationToken cancellationToken = default)
    {
        var item = await _repository.FindByIdAsync(itemId, cancellationToken);
        if (item is null)
        {
            _logger.LogWarning("Fail: bilinmeyen QueueItem {ItemId}.", itemId);
            return null;
        }

        item.AttemptCount++;
        item.ErrorDetail = errorDetail;

        if (isBusinessException)
        {
            // İş kuralı istisnası: yeniden denenmez (Spec Bölüm 7 — retry yalnızca sistem hatası).
            item.Status = QueueItemStatus.BusinessException;
            item.CompletedAt = DateTime.UtcNow;
            _logger.LogInformation("QueueItem {ItemId} iş kuralı istisnası — yeniden denenmeyecek.", item.Id);
            await _repository.SaveChangesAsync(cancellationToken);
            return item;
        }

        var queue = await _repository.FindQueueAsync(item.QueueId, cancellationToken);
        var maxRetries = queue?.MaxRetries ?? 0;

        if (item.AttemptCount <= maxRetries)
        {
            // Yeniden dene: kalemi tekrar kuyruğa al, atamayı temizle.
            item.Status = QueueItemStatus.New;
            item.AssignedRobotId = null;
            item.StartedAt = null;
            _logger.LogWarning("QueueItem {ItemId} sistem hatası — yeniden kuyruğa alındı (deneme {Attempt}/{Max}).",
                item.Id, item.AttemptCount, maxRetries);
        }
        else
        {
            item.Status = QueueItemStatus.Failed;
            item.CompletedAt = DateTime.UtcNow;
            _logger.LogError("QueueItem {ItemId} maksimum deneme aşıldı ({Attempt}/{Max}) — Failed.",
                item.Id, item.AttemptCount, maxRetries);
        }

        await _repository.SaveChangesAsync(cancellationToken);
        return item;
    }
}

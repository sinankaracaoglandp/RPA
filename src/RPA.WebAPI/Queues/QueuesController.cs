namespace RPA.WebAPI.Queues;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;

/// <summary>
/// Kuyruk motoru endpoint'leri (Task 3.2, Spec Bölüm 7). Robotlar sıradaki kalemi UPDLOCK ile
/// çeker (nextitem) ve durum geçişini (complete/fail) PATCH ile bildirir.
/// </summary>
[ApiController]
[Route("api/queues")]
[Authorize]
public class QueuesController : ControllerBase
{
    private readonly IQueueService _queueService;

    public QueuesController(IQueueService queueService)
    {
        _queueService = queueService;
    }

    /// <summary>
    /// Kuyruktan sıradaki kalemi robota atayarak çeker. Uygun kalem yoksa 204 No Content.
    /// </summary>
    [HttpGet("{id:guid}/nextitem")]
    [ProducesResponseType(typeof(QueueItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetNextItem(Guid id, [FromQuery] Guid robotId, CancellationToken ct)
    {
        if (robotId == Guid.Empty)
            return BadRequest(new { error = "robotId zorunludur." });

        var item = await _queueService.GetNextItemAsync(id, robotId, ct);
        return item is null ? NoContent() : Ok(QueueItemDto.From(item));
    }

    /// <summary>
    /// Orchestrator Kuyruklar ekranı (WP-6.1): tüm kuyrukları durum bazlı kalem sayaçlarıyla listeler.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<QueueSummary>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListQueues(CancellationToken ct)
    {
        var summaries = await _queueService.ListQueuesAsync(ct);
        return Ok(summaries);
    }

    /// <summary>
    /// Orchestrator Kuyruklar ekranı (WP-6.1): kuyruğun kalemlerini opsiyonel durum filtresiyle
    /// sayfalı listeler (en yeni önce). status geçersizse 400.
    /// </summary>
    [HttpGet("{id:guid}/items")]
    [ProducesResponseType(typeof(QueueItemListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListItems(
        Guid id,
        [FromQuery] string? status,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        QueueItemStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<QueueItemStatus>(status, ignoreCase: true, out var parsed))
                return BadRequest(new { error = $"Geçersiz status: {status}" });
            statusFilter = parsed;
        }

        take = take is <= 0 or > 200 ? 50 : take;
        var page = await _queueService.ListItemsAsync(id, statusFilter, skip, take, ct);

        return Ok(new QueueItemListResponse
        {
            TotalCount = page.TotalCount,
            Items = page.Items.Select(QueueItemDto.From).ToList(),
        });
    }

    /// <summary>Tek bir kuyruk kaleminin son durumunu getirir. Yanlış kuyruk altında istenirse 404 döner.</summary>
    [HttpGet("{id:guid}/items/{itemId:guid}")]
    [ProducesResponseType(typeof(QueueItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetItem(Guid id, Guid itemId, CancellationToken ct)
    {
        var item = await _queueService.GetItemAsync(itemId, ct);
        return item is null || item.QueueId != id ? NotFound() : Ok(QueueItemDto.From(item));
    }

    /// <summary>
    /// Kalemin durumunu günceller: status = "Successful" tamamlar; "Failed"/"BusinessException"
    /// hata olarak işaretler (isBusinessException'a göre retry uygulanır).
    /// </summary>
    [HttpPatch("{id:guid}/items/{itemId:guid}")]
    [ProducesResponseType(typeof(QueueItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateItem(Guid id, Guid itemId, [FromBody] UpdateQueueItemRequest request, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Status))
            return BadRequest(new { error = "status zorunludur." });

        QueueItem? result;
        switch (request.Status.Trim().ToLowerInvariant())
        {
            case "successful":
            case "complete":
            case "done":
                result = await _queueService.CompleteAsync(itemId, ct);
                break;
            case "businessexception":
                result = await _queueService.FailAsync(itemId, request.ErrorDetail, isBusinessException: true, ct);
                break;
            case "failed":
            case "fail":
                result = await _queueService.FailAsync(itemId, request.ErrorDetail, isBusinessException: false, ct);
                break;
            default:
                return BadRequest(new { error = $"Geçersiz status: {request.Status}" });
        }

        return result is null ? NotFound() : Ok(QueueItemDto.From(result));
    }
}

public class QueueItemListResponse
{
    public int TotalCount { get; set; }
    public List<QueueItemDto> Items { get; set; } = new();
}

public class UpdateQueueItemRequest
{
    public string Status { get; set; } = string.Empty;
    public string? ErrorDetail { get; set; }
}

public class QueueItemDto
{
    public Guid Id { get; set; }
    public Guid QueueId { get; set; }
    public string Status { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public Guid? AssignedRobotId { get; set; }
    public string Payload { get; set; } = "{}";
    public string? ErrorDetail { get; set; }

    public static QueueItemDto From(QueueItem i) => new()
    {
        Id = i.Id,
        QueueId = i.QueueId,
        Status = i.Status.ToString(),
        AttemptCount = i.AttemptCount,
        AssignedRobotId = i.AssignedRobotId,
        Payload = i.Payload,
        ErrorDetail = i.ErrorDetail,
    };
}

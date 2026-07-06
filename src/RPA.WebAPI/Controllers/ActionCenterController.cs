namespace RPA.WebAPI.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RPA.Domain.Entities;
using RPA.Infrastructure.ActionCenter;

/// <summary>
/// Action Center uç noktaları (WP-6.2, Spec Bölüm 8.2): bekleyen kayıtları listeler; atama ve
/// çözümleme (not) işlemlerini yürütür. Operatör/Yönetici rolleri erişir.
/// </summary>
[ApiController]
[Route("api/action-center")]
[Authorize]
public class ActionCenterController : ControllerBase
{
    private readonly ActionCenterService _service;

    public ActionCenterController(ActionCenterService service)
    {
        _service = service;
    }

    /// <summary>Bekleyen kayıtlar (opsiyonel type filtresi: BusinessException/OtpRequest/Approval).</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ActionItemDto>>> ListPending(
        [FromQuery] string? type, CancellationToken ct)
    {
        var items = await _service.ListPendingAsync(type, ct);
        return Ok(items.Select(Map).ToList());
    }

    /// <summary>Tek kayıt detayı.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ActionItemDto>> Get(Guid id, CancellationToken ct)
    {
        var item = await _service.GetAsync(id, ct);
        return item is null ? NotFound() : Ok(Map(item));
    }

    /// <summary>Kaydı bir kullanıcıya atar.</summary>
    [HttpPost("{id:guid}/assign")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ActionItemDto>> Assign(
        Guid id, [FromBody] AssignActionItemRequest request, CancellationToken ct)
    {
        if (request is null || request.UserId == Guid.Empty)
        {
            return BadRequest(new { error = "'userId' zorunludur." });
        }

        var item = await _service.AssignAsync(id, request.UserId, ct);
        return item is null ? NotFound() : Ok(Map(item));
    }

    /// <summary>Kaydı çözümler (Status = Resolved, not + zaman damgası).</summary>
    [HttpPost("{id:guid}/resolve")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ActionItemDto>> Resolve(
        Guid id, [FromBody] ResolveActionItemRequest? request, CancellationToken ct)
    {
        var item = await _service.ResolveAsync(id, request?.Note, ct);
        return item is null ? NotFound() : Ok(Map(item));
    }

    private static ActionItemDto Map(ActionItem a) => new()
    {
        Id = a.Id,
        Type = a.Type,
        Status = a.Status,
        JobRunId = a.JobRunId,
        QueueItemId = a.QueueItemId,
        AssignedUserId = a.AssignedUserId,
        ResolutionNote = a.ResolutionNote,
        ResolvedAt = a.ResolvedAt,
        TimeoutAt = a.TimeoutAt,
        CreatedAt = a.CreatedAt,
    };
}

public class AssignActionItemRequest
{
    public Guid UserId { get; set; }
}

public class ResolveActionItemRequest
{
    public string? Note { get; set; }
}

public class ActionItemDto
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid? JobRunId { get; set; }
    public Guid? QueueItemId { get; set; }
    public Guid? AssignedUserId { get; set; }
    public string? ResolutionNote { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public DateTime? TimeoutAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

namespace RPA.WebAPI.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RPA.Infrastructure.Services;
using BusinessException = RPA.Domain.Exceptions.BusinessException;

/// <summary>Studio designer "Run" isteğini Agent kuyruk yürütme yoluna alır.</summary>
[ApiController]
[Route("api/workflows/{workflowId}/run")]
[Authorize]
public sealed class WorkflowRunController : ControllerBase
{
    private readonly WorkflowRunService _service;

    public WorkflowRunController(WorkflowRunService service)
    {
        _service = service;
    }

    [HttpPost]
    [ProducesResponseType(typeof(WorkflowRunResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<WorkflowRunResponse>> Run(
        string workflowId,
        [FromBody] WorkflowRunRequest? request,
        CancellationToken ct)
    {
        if (!Guid.TryParse(workflowId, out var id))
        {
            return BadRequest(new { error = "'workflowId' geçerli bir GUID olmalıdır." });
        }

        try
        {
            var result = await _service.EnqueueDraftAsync(id, request?.Arguments, ct);
            return Ok(new WorkflowRunResponse
            {
                QueueItemId = result.QueueItemId,
                QueueId = result.QueueId,
                Status = result.Status,
            });
        }
        catch (BusinessException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}

public sealed class WorkflowRunRequest
{
    public Dictionary<string, object?> Arguments { get; set; } = new();
}

public sealed class WorkflowRunResponse
{
    public Guid QueueItemId { get; set; }
    public Guid QueueId { get; set; }
    public string Status { get; set; } = string.Empty;
}

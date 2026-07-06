namespace RPA.WebAPI.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RPA.Domain.Entities;
using RPA.Infrastructure.Services;

/// <summary>
/// Workflow deployment governance uç noktaları (WP-6.4, Spec Bölüm 9).
/// Yaşam döngüsü: Draft → (publish) → Test → (approve) → Published (Prod).
/// </summary>
[ApiController]
[Route("api/workflows/{workflowId}/versions")]
[Authorize]
public class WorkflowDeploymentController : ControllerBase
{
    private readonly WorkflowDeploymentService _service;

    public WorkflowDeploymentController(WorkflowDeploymentService service)
    {
        _service = service;
    }

    /// <summary>Bir workflow'un tüm versiyonlarını (deployment durumu ile) listeler.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<WorkflowVersionDto>>> List(string workflowId, CancellationToken ct)
    {
        if (!Guid.TryParse(workflowId, out var id))
        {
            return BadRequest(new { error = "'workflowId' geçerli bir GUID olmalıdır." });
        }

        var versions = await _service.ListVersionsAsync(id, ct);
        return Ok(versions.Select(Map).ToList());
    }

    /// <summary>Bir versiyonu Test ortamına yayınlar (yalnızca "Developer" rolü).</summary>
    [Authorize(Roles = WorkflowDeploymentService.DeveloperRole)]
    [HttpPost("{version}/publish")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<WorkflowVersionDto>> Publish(
        string workflowId, string version, CancellationToken ct)
    {
        if (!Guid.TryParse(workflowId, out var id))
        {
            return BadRequest(new { error = "'workflowId' geçerli bir GUID olmalıdır." });
        }

        var result = await _service.PublishToTestAsync(id, version, CallerRoles(), ct);
        return Ok(Map(result));
    }

    /// <summary>Test'teki bir versiyonu onaylayıp Prod'a terfi ettirir (yalnızca "Approver" rolü).</summary>
    [Authorize(Roles = WorkflowDeploymentService.ApproverRole)]
    [HttpPost("{version}/approve")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<WorkflowVersionDto>> Approve(
        string workflowId, string version, CancellationToken ct)
    {
        if (!Guid.TryParse(workflowId, out var id))
        {
            return BadRequest(new { error = "'workflowId' geçerli bir GUID olmalıdır." });
        }

        var result = await _service.ApproveToProdAsync(id, version, CallerRoles(), ct);
        return Ok(Map(result));
    }

    private List<string> CallerRoles() => User.Claims
        .Where(c => c.Type == System.Security.Claims.ClaimTypes.Role)
        .Select(c => c.Value)
        .ToList();

    private static WorkflowVersionDto Map(WorkflowVersion v) => new()
    {
        Id = v.Id,
        WorkflowId = v.WorkflowId,
        Version = v.Version,
        Status = v.Status.ToString(),
        EnvironmentId = v.EnvironmentId,
        ChangeNotes = v.ChangeNotes,
    };
}

public class WorkflowVersionDto
{
    public Guid Id { get; set; }
    public Guid WorkflowId { get; set; }
    public string Version { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid EnvironmentId { get; set; }
    public string? ChangeNotes { get; set; }
}

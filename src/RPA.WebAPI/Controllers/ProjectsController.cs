namespace RPA.WebAPI.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RPA.Domain.Entities;
using RPA.Infrastructure.Services;
using BusinessException = RPA.Domain.Exceptions.BusinessException;

/// <summary>Studio Projelerim uç noktaları (Paket B — proje/workflow kalıcılığı).</summary>
[ApiController]
[Route("api/projects")]
[Authorize]
public class ProjectsController : ControllerBase
{
    private readonly WorkflowDesignService _service;

    public ProjectsController(WorkflowDesignService service) => _service = service;

    /// <summary>Proje listesi (workflow sayılarıyla).</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ProjectDto>>> List(CancellationToken ct)
    {
        var projects = await _service.ListProjectsAsync(ct);
        return Ok(projects.Select(p => Map(p.Project, p.WorkflowCount)).ToList());
    }

    /// <summary>Yeni proje oluşturur.</summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProjectDto>> Create(
        [FromBody] CreateProjectRequest request, CancellationToken ct)
    {
        try
        {
            var project = await _service.CreateProjectAsync(request.Name, request.Description, ct);
            return Ok(Map(project, 0));
        }
        catch (BusinessException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Projedeki workflow'lar (son güncellenme sırasıyla).</summary>
    [HttpGet("{projectId}/workflows")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<WorkflowSummaryDto>>> ListWorkflows(
        string projectId, CancellationToken ct)
    {
        if (!Guid.TryParse(projectId, out var id))
        {
            return BadRequest(new { error = "'projectId' geçerli bir GUID olmalıdır." });
        }
        try
        {
            var workflows = await _service.ListWorkflowsAsync(id, ct);
            return Ok(workflows.Select(Map).ToList());
        }
        catch (BusinessException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Projede workflow oluşturur (boş taslak versiyonla).</summary>
    [HttpPost("{projectId}/workflows")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<WorkflowSummaryDto>> CreateWorkflow(
        string projectId, [FromBody] CreateWorkflowRequest request, CancellationToken ct)
    {
        if (!Guid.TryParse(projectId, out var id))
        {
            return BadRequest(new { error = "'projectId' geçerli bir GUID olmalıdır." });
        }
        try
        {
            var workflow = await _service.CreateWorkflowAsync(id, request.Name, ct);
            return Ok(Map(workflow));
        }
        catch (BusinessException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    private static ProjectDto Map(Project p, int workflowCount) => new()
    {
        Id = p.Id, Name = p.Name, Description = p.Description, WorkflowCount = workflowCount,
    };

    private static WorkflowSummaryDto Map(Workflow w) => new()
    {
        Id = w.Id, Name = w.Name, UpdatedAt = w.UpdatedAt,
    };
}

public class CreateProjectRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class CreateWorkflowRequest
{
    public string Name { get; set; } = string.Empty;
}

public class ProjectDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int WorkflowCount { get; set; }
}

public class WorkflowSummaryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime? UpdatedAt { get; set; }
}

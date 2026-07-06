namespace RPA.WebAPI.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RPA.Domain.Entities;
using RPA.Domain.Interfaces;

/// <summary>
/// Orchestrator İşler (JobRun) + Dashboard read-side uç noktaları (WP-6.1, Spec Bölüm 8.2).
/// Yalnızca okuma; iş yürütme agent protokolü üzerinden yapılır.
/// </summary>
[ApiController]
[Route("api/jobruns")]
[Authorize]
public class JobRunsController : ControllerBase
{
    private readonly IJobRunQueryRepository _query;

    public JobRunsController(IJobRunQueryRepository query)
    {
        _query = query;
    }

    /// <summary>İşler ekranı: filtreli + sayfalı JobRun listesi (StartedAt azalan).</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<JobRunListResponse>> List(
        [FromQuery] string? status,
        [FromQuery] Guid? environmentId,
        [FromQuery] Guid? robotId,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        take = take is <= 0 or > 200 ? 50 : take;
        var page = await _query.ListAsync(
            new JobRunQuery(status, environmentId, robotId, fromUtc, toUtc, skip, take), ct);

        return Ok(new JobRunListResponse
        {
            TotalCount = page.TotalCount,
            Items = page.Items.Select(Map).ToList(),
        });
    }

    /// <summary>İş detayı.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<JobRunDto>> Get(Guid id, CancellationToken ct)
    {
        var job = await _query.GetByIdAsync(id, ct);
        return job is null ? NotFound() : Ok(Map(job));
    }

    /// <summary>Dashboard özeti: gün (UTC, varsayılan bugün) + opsiyonel ortam.</summary>
    [HttpGet("dashboard")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardSummary>> Dashboard(
        [FromQuery] DateTime? dayUtc,
        [FromQuery] Guid? environmentId,
        CancellationToken ct = default)
    {
        var summary = await _query.GetDashboardSummaryAsync(
            dayUtc ?? DateTime.UtcNow, environmentId, ct);
        return Ok(summary);
    }

    private static JobRunDto Map(JobRun j) => new()
    {
        Id = j.Id,
        WorkflowVersionId = j.WorkflowVersionId,
        Status = j.Status,
        TriggeredBy = j.TriggeredBy,
        AssignedRobotId = j.AssignedRobotId,
        EnvironmentId = j.EnvironmentId,
        StartedAt = j.StartedAt,
        CompletedAt = j.CompletedAt,
        CorrelationId = j.ElasticsearchCorrelationId,
    };
}

public class JobRunListResponse
{
    public int TotalCount { get; set; }
    public List<JobRunDto> Items { get; set; } = new();
}

public class JobRunDto
{
    public Guid Id { get; set; }
    public Guid WorkflowVersionId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string TriggeredBy { get; set; } = string.Empty;
    public Guid? AssignedRobotId { get; set; }
    public Guid EnvironmentId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
}

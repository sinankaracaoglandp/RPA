namespace RPA.WebAPI.Triggers;

using Microsoft.AspNetCore.Mvc;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;

/// <summary>
/// Tetikleyici yönetim endpoint'leri (Task 3.3, Spec Bölüm 7). Tetikleyici oluşturma/güncelleme
/// ve workflow versiyonuna göre listeleme sağlar. Ateşleme (fire) SchedulerHostedService
/// (cron) veya harici çağıranlar (manuel/webhook) tarafından ITriggerService üzerinden yapılır.
/// </summary>
[ApiController]
[Route("api")]
public class TriggersController : ControllerBase
{
    private readonly ITriggerRepository _repository;
    private readonly ITriggerService _triggerService;

    public TriggersController(ITriggerRepository repository, ITriggerService triggerService)
    {
        _repository = repository;
        _triggerService = triggerService;
    }

    /// <summary>Yeni tetikleyici oluşturur. Type=Cron ise Schedule zorunludur.</summary>
    [HttpPost("triggers")]
    [ProducesResponseType(typeof(TriggerDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateTriggerRequest request, CancellationToken ct)
    {
        if (request is null || request.WorkflowVersionId == Guid.Empty)
            return BadRequest(new { error = "workflowVersionId zorunludur." });

        if (!Enum.TryParse<TriggerType>(request.Type, ignoreCase: true, out var type))
            return BadRequest(new { error = $"Geçersiz Type: {request.Type}" });

        if (type == TriggerType.Cron && request.Schedule is null)
            return BadRequest(new { error = "Cron tetikleyici için Schedule zorunludur." });

        var trigger = new Trigger
        {
            ProjectId = request.ProjectId,
            WorkflowVersionId = request.WorkflowVersionId,
            Type = type,
            Configuration = request.Configuration ?? "{}",
            EnvironmentId = request.EnvironmentId,
            IsActive = request.IsActive,
        };
        await _repository.AddTriggerAsync(trigger, ct);

        Schedule? schedule = null;
        if (type == TriggerType.Cron && request.Schedule is not null)
        {
            schedule = new Schedule
            {
                TriggerId = trigger.Id,
                CronExpression = request.Schedule.CronExpression,
                TimeZone = string.IsNullOrWhiteSpace(request.Schedule.TimeZone) ? "UTC" : request.Schedule.TimeZone,
                OverlapPolicy = string.IsNullOrWhiteSpace(request.Schedule.OverlapPolicy) ? "skip" : request.Schedule.OverlapPolicy,
            };
            await _repository.AddScheduleAsync(schedule, ct);
        }

        await _repository.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(GetByWorkflowVersion), new { workflowVersionId = trigger.WorkflowVersionId }, TriggerDto.From(trigger, schedule));
    }

    /// <summary>Belirtilen workflow versiyonuna ait tüm tetikleyicileri döner.</summary>
    [HttpGet("workflows/{workflowVersionId:guid}/triggers")]
    [ProducesResponseType(typeof(List<TriggerDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByWorkflowVersion(Guid workflowVersionId, CancellationToken ct)
    {
        var triggers = await _repository.FindTriggersByWorkflowVersionAsync(workflowVersionId, ct);
        var dtos = new List<TriggerDto>();
        foreach (var trigger in triggers)
        {
            var schedule = trigger.Type == TriggerType.Cron
                ? await _repository.FindScheduleByTriggerIdAsync(trigger.Id, ct)
                : null;
            dtos.Add(TriggerDto.From(trigger, schedule));
        }

        return Ok(dtos);
    }

    /// <summary>Tetikleyiciyi günceller: IsActive, Configuration ve (varsa) Schedule alanları.</summary>
    [HttpPatch("triggers/{id:guid}")]
    [ProducesResponseType(typeof(TriggerDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTriggerRequest request, CancellationToken ct)
    {
        var trigger = await _repository.FindTriggerByIdAsync(id, ct);
        if (trigger is null)
            return NotFound();

        if (request.IsActive.HasValue)
            trigger.IsActive = request.IsActive.Value;
        if (request.Configuration is not null)
            trigger.Configuration = request.Configuration;

        Schedule? schedule = await _repository.FindScheduleByTriggerIdAsync(id, ct);
        if (request.Schedule is not null && schedule is not null)
        {
            if (!string.IsNullOrWhiteSpace(request.Schedule.CronExpression))
                schedule.CronExpression = request.Schedule.CronExpression;
            if (!string.IsNullOrWhiteSpace(request.Schedule.TimeZone))
                schedule.TimeZone = request.Schedule.TimeZone;
            if (!string.IsNullOrWhiteSpace(request.Schedule.OverlapPolicy))
                schedule.OverlapPolicy = request.Schedule.OverlapPolicy;
        }

        await _repository.SaveChangesAsync(ct);
        return Ok(TriggerDto.From(trigger, schedule));
    }

    /// <summary>Tetikleyiciyi manuel olarak ateşler (test/elle tetikleme amaçlı).</summary>
    [HttpPost("triggers/{id:guid}/fire")]
    [ProducesResponseType(typeof(TriggerFireResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Fire(Guid id, CancellationToken ct)
    {
        var result = await _triggerService.ExecuteTriggerAsync(id, "manual", ct);
        if (result.Outcome == TriggerExecutionOutcome.NotFound)
            return NotFound();

        return Ok(new TriggerFireResultDto
        {
            Outcome = result.Outcome.ToString(),
            JobRunId = result.JobRun?.Id,
            JobRunStatus = result.JobRun?.Status,
        });
    }
}

public sealed class CreateTriggerRequest
{
    public Guid ProjectId { get; set; }
    public Guid WorkflowVersionId { get; set; }
    public string Type { get; set; } = "Manual";
    public string? Configuration { get; set; }
    public Guid EnvironmentId { get; set; }
    public bool IsActive { get; set; } = true;
    public ScheduleRequest? Schedule { get; set; }
}

public sealed class UpdateTriggerRequest
{
    public bool? IsActive { get; set; }
    public string? Configuration { get; set; }
    public ScheduleRequest? Schedule { get; set; }
}

public sealed class ScheduleRequest
{
    public string CronExpression { get; set; } = "";
    public string TimeZone { get; set; } = "UTC";
    public string OverlapPolicy { get; set; } = "skip";
}

public sealed class TriggerDto
{
    public Guid Id { get; set; }
    public Guid WorkflowVersionId { get; set; }
    public string Type { get; set; } = "";
    public string Configuration { get; set; } = "{}";
    public bool IsActive { get; set; }
    public ScheduleDto? Schedule { get; set; }

    public static TriggerDto From(Trigger t, Schedule? s) => new()
    {
        Id = t.Id,
        WorkflowVersionId = t.WorkflowVersionId,
        Type = t.Type.ToString(),
        Configuration = t.Configuration,
        IsActive = t.IsActive,
        Schedule = s is null ? null : new ScheduleDto
        {
            CronExpression = s.CronExpression,
            TimeZone = s.TimeZone,
            OverlapPolicy = s.OverlapPolicy,
        },
    };
}

public sealed class ScheduleDto
{
    public string CronExpression { get; set; } = "";
    public string TimeZone { get; set; } = "UTC";
    public string OverlapPolicy { get; set; } = "skip";
}

public sealed class TriggerFireResultDto
{
    public string Outcome { get; set; } = "";
    public Guid? JobRunId { get; set; }
    public string? JobRunStatus { get; set; }
}

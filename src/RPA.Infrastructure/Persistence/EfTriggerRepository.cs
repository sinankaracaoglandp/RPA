namespace RPA.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using RPA.Domain.Entities;
using RPA.Domain.Interfaces;

/// <summary>EF Core tabanlı <see cref="ITriggerRepository"/> implementasyonu (Task 3.3).</summary>
public sealed class EfTriggerRepository : ITriggerRepository
{
    private readonly RpaDbContext _db;

    public EfTriggerRepository(RpaDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public Task<Trigger?> FindTriggerByIdAsync(Guid triggerId, CancellationToken cancellationToken = default)
        => _db.Triggers.FirstOrDefaultAsync(t => t.Id == triggerId && !t.IsDeleted, cancellationToken);

    public async Task<IReadOnlyList<Trigger>> FindTriggersByWorkflowVersionAsync(Guid workflowVersionId, CancellationToken cancellationToken = default)
        => await _db.Triggers
            .Where(t => !t.IsDeleted && t.WorkflowVersionId == workflowVersionId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Trigger>> FindActiveCronTriggersAsync(CancellationToken cancellationToken = default)
        => await _db.Triggers
            .Where(t => !t.IsDeleted && t.IsActive && t.Type == RPA.Domain.Enums.TriggerType.Cron)
            .ToListAsync(cancellationToken);

    public Task<Schedule?> FindScheduleByTriggerIdAsync(Guid triggerId, CancellationToken cancellationToken = default)
        => _db.Schedules.FirstOrDefaultAsync(s => s.TriggerId == triggerId && !s.IsDeleted, cancellationToken);

    public async Task AddTriggerAsync(Trigger trigger, CancellationToken cancellationToken = default)
        => await _db.Triggers.AddAsync(trigger, cancellationToken);

    public async Task AddScheduleAsync(Schedule schedule, CancellationToken cancellationToken = default)
        => await _db.Schedules.AddAsync(schedule, cancellationToken);

    public Task<bool> HasRunningJobRunAsync(Guid workflowVersionId, CancellationToken cancellationToken = default)
        => _db.JobRuns.AnyAsync(j => j.WorkflowVersionId == workflowVersionId && j.Status == "Running", cancellationToken);

    public Task<JobRun?> FindOldestQueuedJobRunAsync(Guid workflowVersionId, CancellationToken cancellationToken = default)
        => _db.JobRuns
            .Where(j => j.WorkflowVersionId == workflowVersionId && j.Status == "Queued")
            .OrderBy(j => j.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task AddJobRunAsync(JobRun jobRun, CancellationToken cancellationToken = default)
        => await _db.JobRuns.AddAsync(jobRun, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _db.SaveChangesAsync(cancellationToken);
}

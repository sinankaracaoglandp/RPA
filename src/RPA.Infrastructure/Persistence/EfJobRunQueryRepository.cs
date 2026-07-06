namespace RPA.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using RPA.Domain.Entities;
using RPA.Domain.Interfaces;

/// <summary>
/// EF Core tabanlı JobRun read-side sorgu repository'si (WP-6.1a). Yalnızca okuma;
/// AsNoTracking ile izleme yükü olmadan Orchestrator İşler/Dashboard ekranlarını besler.
/// </summary>
public sealed class EfJobRunQueryRepository : IJobRunQueryRepository
{
    private readonly RpaDbContext _db;

    public EfJobRunQueryRepository(RpaDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<JobRunPage> ListAsync(JobRunQuery query, CancellationToken cancellationToken = default)
    {
        var q = _db.JobRuns.AsNoTracking().Where(j => !j.IsDeleted);

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            q = q.Where(j => j.Status == query.Status);
        }
        if (query.EnvironmentId is { } env)
        {
            q = q.Where(j => j.EnvironmentId == env);
        }
        if (query.RobotId is { } robot)
        {
            q = q.Where(j => j.AssignedRobotId == robot);
        }
        if (query.FromUtc is { } from)
        {
            q = q.Where(j => j.StartedAt >= from);
        }
        if (query.ToUtc is { } to)
        {
            q = q.Where(j => j.StartedAt < to);
        }

        var total = await q.CountAsync(cancellationToken).ConfigureAwait(false);

        var take = query.Take <= 0 ? 50 : query.Take;
        var skip = query.Skip < 0 ? 0 : query.Skip;

        var items = await q
            .OrderByDescending(j => j.StartedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new JobRunPage(items, total);
    }

    public async Task<JobRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _db.JobRuns.AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == id && !j.IsDeleted, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<DashboardSummary> GetDashboardSummaryAsync(
        DateTime dayUtc, Guid? environmentId, CancellationToken cancellationToken = default)
    {
        var dayStart = dayUtc.Date;
        var dayEnd = dayStart.AddDays(1);

        var q = _db.JobRuns.AsNoTracking()
            .Where(j => !j.IsDeleted && j.StartedAt >= dayStart && j.StartedAt < dayEnd);

        if (environmentId is { } env)
        {
            q = q.Where(j => j.EnvironmentId == env);
        }

        // Tek sorguda durum bazlı sayım.
        var counts = await q
            .GroupBy(j => j.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        int CountOf(string status) => counts.FirstOrDefault(c => c.Status == status)?.Count ?? 0;

        var successful = CountOf("Successful");
        var failed = CountOf("Failed");
        var businessException = CountOf("BusinessException");
        var running = CountOf("Running");
        var abandoned = CountOf("Abandoned");
        var total = counts.Sum(c => c.Count);

        // Başarı oranı yalnızca tamamlanmış işler üzerinden (Running hariç).
        var completed = total - running;
        var successRate = completed > 0
            ? Math.Round(successful * 100.0 / completed, 2)
            : 0.0;

        return new DashboardSummary(
            total, running, successful, failed, businessException, abandoned, successRate);
    }
}

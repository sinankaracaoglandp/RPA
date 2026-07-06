namespace RPA.Infrastructure.Alerting;

using Microsoft.EntityFrameworkCore;
using RPA.Domain.Enums;
using RPA.Infrastructure.Persistence;

/// <summary>Alarm metriklerini üreten kaynak soyutlaması (WP-6.3).</summary>
public interface IAlertMetricsProvider
{
    Task<AlertMetrics> GetAsync(TimeSpan window, CancellationToken cancellationToken = default);
}

/// <summary>
/// EF Core tabanlı metrik sağlayıcı (WP-6.3): verilen zaman penceresindeki System/Business
/// exception JobRun sayıları, o an offline robot sayısı ve SLA aşan (InProgress + StartedAt
/// kuyruğun SlaSeconds'ından eski) kuyruk kalemi sayısı.
/// </summary>
public sealed class AlertMetricsProvider : IAlertMetricsProvider
{
    private readonly RpaDbContext _db;

    public AlertMetricsProvider(RpaDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<AlertMetrics> GetAsync(TimeSpan window, CancellationToken cancellationToken = default)
    {
        var since = DateTime.UtcNow - window;
        var now = DateTime.UtcNow;

        var systemExceptions = await _db.JobRuns.AsNoTracking()
            .CountAsync(j => !j.IsDeleted && j.Status == "Failed" && j.StartedAt >= since, cancellationToken)
            .ConfigureAwait(false);

        var businessExceptions = await _db.JobRuns.AsNoTracking()
            .CountAsync(j => !j.IsDeleted && j.Status == "BusinessException" && j.StartedAt >= since, cancellationToken)
            .ConfigureAwait(false);

        var robotsOffline = await _db.Robots.AsNoTracking()
            .CountAsync(r => !r.IsDeleted && r.Status == RobotStatus.Offline, cancellationToken)
            .ConfigureAwait(false);

        // SLA aşımı: InProgress kalemler, kuyruğun SlaSeconds'ından daha uzun süredir çalışıyor.
        var slaBreaches = await _db.QueueItems.AsNoTracking()
            .Where(i => !i.IsDeleted && i.Status == QueueItemStatus.InProgress && i.StartedAt != null)
            .Join(_db.Queues.AsNoTracking().Where(q => q.SlaSeconds != null),
                  i => i.QueueId, q => q.Id,
                  (i, q) => new { i.StartedAt, q.SlaSeconds })
            .CountAsync(x => x.StartedAt!.Value.AddSeconds(x.SlaSeconds!.Value) < now, cancellationToken)
            .ConfigureAwait(false);

        return new AlertMetrics(systemExceptions, businessExceptions, robotsOffline, slaBreaches);
    }
}

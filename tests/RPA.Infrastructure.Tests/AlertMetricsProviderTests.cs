namespace RPA.Infrastructure.Tests;

using Microsoft.EntityFrameworkCore;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Infrastructure.Alerting;
using RPA.Infrastructure.Persistence;

/// <summary>WP-6.3 — AlertMetricsProvider: pencere içi exception sayıları, offline robot, SLA aşımı.</summary>
public class AlertMetricsProviderTests
{
    private static RpaDbContext Db()
    {
        var options = new DbContextOptionsBuilder<RpaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new RpaDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task GetAsync_CountsExceptionsOfflineAndSlaBreach()
    {
        using var db = Db();
        var now = DateTime.UtcNow;

        db.JobRuns.AddRange(
            new JobRun { Status = "Failed", StartedAt = now.AddMinutes(-5), ElasticsearchCorrelationId = "1" },
            new JobRun { Status = "Failed", StartedAt = now.AddMinutes(-10), ElasticsearchCorrelationId = "2" },
            new JobRun { Status = "BusinessException", StartedAt = now.AddMinutes(-2), ElasticsearchCorrelationId = "3" },
            new JobRun { Status = "Failed", StartedAt = now.AddHours(-3), ElasticsearchCorrelationId = "old" }); // pencere dışı

        db.Robots.Add(new Robot { MachineName = "R1", Status = RobotStatus.Offline });
        db.Robots.Add(new Robot { MachineName = "R2", Status = RobotStatus.Online });

        var q = new Queue { Name = "Q1", SlaSeconds = 60 };
        db.Queues.Add(q);
        db.QueueItems.Add(new QueueItem { QueueId = q.Id, IdempotencyKey = "a", Status = QueueItemStatus.InProgress, StartedAt = now.AddMinutes(-5) }); // SLA aştı
        db.QueueItems.Add(new QueueItem { QueueId = q.Id, IdempotencyKey = "b", Status = QueueItemStatus.InProgress, StartedAt = now.AddSeconds(-10) }); // aşmadı
        await db.SaveChangesAsync();

        var metrics = await new AlertMetricsProvider(db).GetAsync(TimeSpan.FromHours(1));

        Assert.Equal(2, metrics.SystemExceptionCount);
        Assert.Equal(1, metrics.BusinessExceptionCount);
        Assert.Equal(1, metrics.RobotOfflineCount);
        Assert.Equal(1, metrics.QueueSlaBreachCount);
    }
}

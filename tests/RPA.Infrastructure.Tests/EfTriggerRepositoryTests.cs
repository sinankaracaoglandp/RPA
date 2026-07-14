using Microsoft.EntityFrameworkCore;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Infrastructure.Persistence;
using Xunit;

namespace RPA.Infrastructure.Tests;

public class EfTriggerRepositoryTests
{
    private static RpaDbContext NewDb() =>
        new(new DbContextOptionsBuilder<RpaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task ListTriggersAsync_FiltersByProjectAndActive()
    {
        using var db = NewDb();
        var projectId = Guid.NewGuid();
        db.Triggers.Add(new Trigger { ProjectId = projectId, IsActive = true, Type = TriggerType.Cron });
        db.Triggers.Add(new Trigger { ProjectId = projectId, IsActive = false, Type = TriggerType.Manual });
        db.Triggers.Add(new Trigger { ProjectId = Guid.NewGuid(), IsActive = true, Type = TriggerType.Cron });
        await db.SaveChangesAsync();
        var repo = new EfTriggerRepository(db);

        var result = await repo.ListTriggersAsync(projectId, null, isActive: true, default);

        Assert.Single(result);
        Assert.Equal(projectId, result[0].ProjectId);
    }

    [Fact]
    public async Task GetActiveJobCountsByRobotAsync_CountsRunningPerRobot()
    {
        using var db = NewDb();
        var robotA = Guid.NewGuid();
        db.JobRuns.Add(new JobRun { AssignedRobotId = robotA, Status = "Running", StartedAt = DateTime.UtcNow });
        db.JobRuns.Add(new JobRun { AssignedRobotId = robotA, Status = "Running", StartedAt = DateTime.UtcNow });
        db.JobRuns.Add(new JobRun { AssignedRobotId = robotA, Status = "Successful", StartedAt = DateTime.UtcNow });
        db.JobRuns.Add(new JobRun { AssignedRobotId = null, Status = "Running", StartedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var repo = new EfTriggerRepository(db);

        var counts = await repo.GetActiveJobCountsByRobotAsync(default);

        Assert.Equal(2, counts[robotA]);
        Assert.False(counts.ContainsKey(Guid.Empty));
    }
}

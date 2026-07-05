namespace RPA.Infrastructure.Tests;

using Microsoft.EntityFrameworkCore;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Persistence;
using RPA.Infrastructure.Robots;

/// <summary>
/// Task 3.1 — RobotRepository ve RobotService kenar durum (edge case) testleri.
/// Soft-delete, null/geçersiz argümanlar, eş zamanlı heartbeat ve offline sınır koşulları.
/// </summary>
public class RobotServiceEdgeCaseTests
{
    private static RpaDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<RpaDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var db = new RpaDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static RobotService CreateService(RpaDbContext db)
        => new RobotService(new EfRobotRepository(db), new MockLogger<RobotService>());

    // ---- Constructor guards ----

    [Fact]
    public void Repository_NullContext_Throws()
        => Assert.Throws<ArgumentNullException>(() => new EfRobotRepository(null!));

    [Fact]
    public void Service_NullRepository_Throws()
        => Assert.Throws<ArgumentNullException>(() => new RobotService(null!, new MockLogger<RobotService>()));

    [Fact]
    public void Service_NullLogger_Throws()
    {
        var db = CreateInMemoryDb();
        Assert.Throws<ArgumentNullException>(() => new RobotService(new EfRobotRepository(db), null!));
    }

    // ---- Register validation ----

    [Fact]
    public async Task Register_NullRequest_Throws()
    {
        var service = CreateService(CreateInMemoryDb());
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.RegisterAsync(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Register_BlankMachineName_Throws(string machineName)
    {
        var service = CreateService(CreateInMemoryDb());
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.RegisterAsync(new RobotRegistrationRequest { MachineName = machineName }));
    }

    // ---- Soft-delete scenarios ----

    [Fact]
    public async Task FindByMachineName_SoftDeleted_ReturnsNull()
    {
        var db = CreateInMemoryDb();
        var repo = new EfRobotRepository(db);
        var robot = new Robot { MachineName = "DEL-01", IsDeleted = true };
        await repo.AddAsync(robot);
        await repo.SaveChangesAsync();

        Assert.Null(await repo.FindByMachineNameAsync("DEL-01"));
    }

    [Fact]
    public async Task FindById_SoftDeleted_ReturnsNull()
    {
        var db = CreateInMemoryDb();
        var repo = new EfRobotRepository(db);
        var robot = new Robot { MachineName = "DEL-02", IsDeleted = true };
        await repo.AddAsync(robot);
        await repo.SaveChangesAsync();

        Assert.Null(await repo.FindByIdAsync(robot.Id));
    }

    [Fact]
    public async Task Register_SoftDeletedMachine_CreatesNewRobot()
    {
        var db = CreateInMemoryDb();
        var repo = new EfRobotRepository(db);
        await repo.AddAsync(new Robot { MachineName = "GHOST", IsDeleted = true });
        await repo.SaveChangesAsync();

        var service = new RobotService(repo, new MockLogger<RobotService>());
        var robot = await service.RegisterAsync(new RobotRegistrationRequest { MachineName = "GHOST" });

        Assert.False(robot.IsDeleted);
        Assert.Equal(RobotStatus.Online, robot.Status);
        // Soft-deleted olan + yeni oluşturulan = 2 kayıt.
        Assert.Equal(2, db.Set<Robot>().Count());
    }

    // ---- FindById edge ----

    [Fact]
    public async Task FindById_EmptyGuid_ReturnsNull()
    {
        var repo = new EfRobotRepository(CreateInMemoryDb());
        Assert.Null(await repo.FindByIdAsync(Guid.Empty));
    }

    [Fact]
    public async Task FindByMachineName_Unknown_ReturnsNull()
    {
        var repo = new EfRobotRepository(CreateInMemoryDb());
        Assert.Null(await repo.FindByMachineNameAsync("NOPE"));
    }

    // ---- Heartbeat concurrency / stale ----

    [Fact]
    public async Task Heartbeat_StaleOfflineRobot_BringsBackOnline()
    {
        var db = CreateInMemoryDb();
        var service = CreateService(db);
        var robot = await service.RegisterAsync(new RobotRegistrationRequest { MachineName = "STALE-HB" });
        robot.Status = RobotStatus.Offline;
        robot.LastHeartbeat = DateTime.UtcNow.AddHours(-2);
        await db.SaveChangesAsync();

        var updated = await service.RecordHeartbeatAsync(robot.Id);

        Assert.NotNull(updated);
        Assert.Equal(RobotStatus.Online, updated!.Status);
        Assert.True(updated.LastHeartbeat > DateTime.UtcNow.AddSeconds(-30));
    }

    [Fact]
    public async Task Heartbeat_ConcurrentCalls_AllResolveOnline()
    {
        var db = CreateInMemoryDb();
        var service = CreateService(db);
        var robot = await service.RegisterAsync(new RobotRegistrationRequest { MachineName = "CONC" });

        // Aynı robot için ardışık heartbeat çağrıları tutarlı Online sonucu vermeli.
        var r1 = await service.RecordHeartbeatAsync(robot.Id);
        var r2 = await service.RecordHeartbeatAsync(robot.Id);
        var r3 = await service.RecordHeartbeatAsync(robot.Id);

        Assert.Equal(RobotStatus.Online, r1!.Status);
        Assert.Equal(RobotStatus.Online, r2!.Status);
        Assert.Equal(RobotStatus.Online, r3!.Status);
        Assert.Single(db.Set<Robot>());
    }

    // ---- Offline detection boundaries ----

    [Fact]
    public async Task DetectOffline_NoStaleRobots_ReturnsZero_AndKeepsOnline()
    {
        var db = CreateInMemoryDb();
        var service = CreateService(db);
        var fresh = await service.RegisterAsync(new RobotRegistrationRequest { MachineName = "FRESH" });
        fresh.LastHeartbeat = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var count = await service.DetectOfflineRobotsAsync(TimeSpan.FromMinutes(5));

        Assert.Equal(0, count);
        Assert.Equal(RobotStatus.Online, (await service.GetAsync(fresh.Id))!.Status);
    }

    [Fact]
    public async Task DetectOffline_AlreadyOffline_NotRecounted()
    {
        var db = CreateInMemoryDb();
        var service = CreateService(db);
        var robot = await service.RegisterAsync(new RobotRegistrationRequest { MachineName = "OFF" });
        robot.Status = RobotStatus.Offline;
        robot.LastHeartbeat = DateTime.UtcNow.AddHours(-1);
        await db.SaveChangesAsync();

        var count = await service.DetectOfflineRobotsAsync(TimeSpan.FromMinutes(5));

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task DetectOffline_NullHeartbeat_Ignored()
    {
        var db = CreateInMemoryDb();
        var service = CreateService(db);
        var robot = await service.RegisterAsync(new RobotRegistrationRequest { MachineName = "NULL-HB" });
        robot.LastHeartbeat = null;
        robot.Status = RobotStatus.Online;
        await db.SaveChangesAsync();

        var count = await service.DetectOfflineRobotsAsync(TimeSpan.FromMinutes(5));

        Assert.Equal(0, count);
        Assert.Equal(RobotStatus.Online, (await service.GetAsync(robot.Id))!.Status);
    }

    [Fact]
    public async Task DetectOffline_MultipleStale_MarksAll()
    {
        var db = CreateInMemoryDb();
        var service = CreateService(db);
        foreach (var name in new[] { "S1", "S2", "S3" })
        {
            var r = await service.RegisterAsync(new RobotRegistrationRequest { MachineName = name });
            r.LastHeartbeat = DateTime.UtcNow.AddMinutes(-30);
        }
        await db.SaveChangesAsync();

        var count = await service.DetectOfflineRobotsAsync(TimeSpan.FromMinutes(5));

        Assert.Equal(3, count);
        Assert.All(db.Set<Robot>(), r => Assert.Equal(RobotStatus.Offline, r.Status));
    }
}

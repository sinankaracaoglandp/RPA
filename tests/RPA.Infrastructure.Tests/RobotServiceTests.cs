namespace RPA.Infrastructure.Tests;

using Microsoft.EntityFrameworkCore;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Persistence;
using RPA.Infrastructure.Robots;

/// <summary>
/// Task 3.1 — Robot kayıt + heartbeat + offline tespiti testleri (Spec Bölüm 5.6, 9).
/// </summary>
public class RobotServiceTests
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

    // ---- Repository / persistence ----

    [Fact]
    public async Task Repository_AddAndFind_RoundTrips()
    {
        var db = CreateInMemoryDb();
        var repo = new EfRobotRepository(db);
        var robot = new Robot { MachineName = "RPA-01", Mode = RobotMode.Unattended };

        await repo.AddAsync(robot);
        await repo.SaveChangesAsync();

        var byName = await repo.FindByMachineNameAsync("RPA-01");
        var byId = await repo.FindByIdAsync(robot.Id);
        Assert.NotNull(byName);
        Assert.NotNull(byId);
        Assert.Equal(robot.Id, byName!.Id);
    }

    [Fact]
    public async Task ListAsync_ReturnsAllRobots_NewestRegisteredFirst()
    {
        var db = CreateInMemoryDb();
        var service = CreateService(db);
        await service.RegisterAsync(new RobotRegistrationRequest { MachineName = "RPA-01", Mode = RobotMode.Unattended });
        await service.RegisterAsync(new RobotRegistrationRequest { MachineName = "RPA-02", Mode = RobotMode.Attended });

        var all = await service.ListAsync();

        Assert.Equal(2, all.Count);
        Assert.Contains(all, r => r.MachineName == "RPA-01");
        Assert.Contains(all, r => r.MachineName == "RPA-02");
    }

    // ---- Register ----

    [Fact]
    public async Task Register_NewMachine_CreatesOnlineRobot()
    {
        var db = CreateInMemoryDb();
        var service = CreateService(db);

        var robot = await service.RegisterAsync(new RobotRegistrationRequest
        {
            MachineName = "RPA-01",
            Mode = RobotMode.Unattended,
            AgentVersion = "1.0.0",
        });

        Assert.NotEqual(Guid.Empty, robot.Id);
        Assert.Equal(RobotStatus.Online, robot.Status);
        Assert.NotNull(robot.LastHeartbeat);
        Assert.Single(db.Set<Robot>());
    }

    [Fact]
    public async Task Register_ExistingMachine_UpdatesInsteadOfDuplicating()
    {
        var db = CreateInMemoryDb();
        var service = CreateService(db);

        var first = await service.RegisterAsync(new RobotRegistrationRequest { MachineName = "RPA-01", AgentVersion = "1.0.0" });
        var second = await service.RegisterAsync(new RobotRegistrationRequest { MachineName = "RPA-01", AgentVersion = "2.0.0" });

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("2.0.0", second.AgentVersion);
        Assert.Single(db.Set<Robot>());
    }

    // ---- Get ----

    [Fact]
    public async Task Get_UnknownId_ReturnsNull()
    {
        var db = CreateInMemoryDb();
        var service = CreateService(db);
        Assert.Null(await service.GetAsync(Guid.NewGuid()));
    }

    // ---- Heartbeat ----

    [Fact]
    public async Task Heartbeat_UpdatesLastHeartbeatAndStatus()
    {
        var db = CreateInMemoryDb();
        var service = CreateService(db);
        var robot = await service.RegisterAsync(new RobotRegistrationRequest { MachineName = "RPA-01" });
        robot.Status = RobotStatus.Offline;
        robot.LastHeartbeat = DateTime.UtcNow.AddMinutes(-10);
        await db.SaveChangesAsync();

        var updated = await service.RecordHeartbeatAsync(robot.Id);

        Assert.NotNull(updated);
        Assert.Equal(RobotStatus.Online, updated!.Status);
        Assert.True(updated.LastHeartbeat > DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task Heartbeat_UnknownRobot_ReturnsNull()
    {
        var db = CreateInMemoryDb();
        var service = CreateService(db);
        Assert.Null(await service.RecordHeartbeatAsync(Guid.NewGuid()));
    }

    // ---- Offline detection ----

    [Fact]
    public async Task DetectOffline_MarksStaleRobotsOffline()
    {
        var db = CreateInMemoryDb();
        var service = CreateService(db);

        var stale = await service.RegisterAsync(new RobotRegistrationRequest { MachineName = "STALE" });
        stale.LastHeartbeat = DateTime.UtcNow.AddMinutes(-6);
        var fresh = await service.RegisterAsync(new RobotRegistrationRequest { MachineName = "FRESH" });
        fresh.LastHeartbeat = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        var count = await service.DetectOfflineRobotsAsync(TimeSpan.FromMinutes(5));

        Assert.Equal(1, count);
        Assert.Equal(RobotStatus.Offline, (await service.GetAsync(stale.Id))!.Status);
        Assert.Equal(RobotStatus.Online, (await service.GetAsync(fresh.Id))!.Status);
    }
}

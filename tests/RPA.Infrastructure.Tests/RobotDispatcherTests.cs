using Moq;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Scheduling;
using Xunit;

namespace RPA.Infrastructure.Tests;

public class RobotDispatcherTests
{
    private static Robot Rbt(string name, string tags, RobotStatus status, int cap, DateTime hb) =>
        new() { Id = Guid.NewGuid(), MachineName = name, Tags = tags, Status = status, Capacity = cap, LastHeartbeat = hb };

    private static IRobotDispatcher Build(IEnumerable<Robot> robots, IReadOnlyDictionary<Guid, int> active)
    {
        var robotSvc = new Mock<IRobotService>();
        robotSvc.Setup(s => s.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(robots.ToList());
        var repo = new Mock<ITriggerRepository>();
        repo.Setup(r => r.GetActiveJobCountsByRobotAsync(It.IsAny<CancellationToken>())).ReturnsAsync(active);
        return new RobotDispatcher(robotSvc.Object, repo.Object);
    }

    [Fact]
    public async Task SelectRobot_RequiresTagCoverage()
    {
        var ok = Rbt("A", "prod-vm,sap,extra", RobotStatus.Online, 1, DateTime.UtcNow);
        var missing = Rbt("B", "prod-vm", RobotStatus.Online, 1, DateTime.UtcNow);
        var d = Build(new[] { missing, ok }, new Dictionary<Guid, int>());

        var result = await d.SelectRobotAsync(new Trigger { TargetRobotTags = "prod-vm,sap" }, default);

        Assert.Equal(ok.Id, result!.Id);
    }

    [Fact]
    public async Task SelectRobot_SkipsOfflineAndFullCapacity()
    {
        var offline = Rbt("A", "x", RobotStatus.Offline, 5, DateTime.UtcNow);
        var full = Rbt("B", "x", RobotStatus.Online, 1, DateTime.UtcNow);
        var free = Rbt("C", "x", RobotStatus.Online, 2, DateTime.UtcNow);
        var d = Build(new[] { offline, full, free },
            new Dictionary<Guid, int> { [full.Id] = 1, [free.Id] = 1 });

        var result = await d.SelectRobotAsync(new Trigger { TargetRobotTags = "x" }, default);

        Assert.Equal(free.Id, result!.Id);
    }

    [Fact]
    public async Task SelectRobot_ReturnsNull_WhenNoCandidate()
    {
        var full = Rbt("B", "x", RobotStatus.Online, 1, DateTime.UtcNow);
        var d = Build(new[] { full }, new Dictionary<Guid, int> { [full.Id] = 1 });

        var result = await d.SelectRobotAsync(new Trigger { TargetRobotTags = "x" }, default);

        Assert.Null(result);
    }

    [Fact]
    public async Task SelectRobot_PrefersMostFreeCapacity()
    {
        var lessFree = Rbt("A", "x", RobotStatus.Online, 3, DateTime.UtcNow); // free 2
        var moreFree = Rbt("B", "x", RobotStatus.Online, 5, DateTime.UtcNow); // free 4
        var d = Build(new[] { lessFree, moreFree },
            new Dictionary<Guid, int> { [lessFree.Id] = 1, [moreFree.Id] = 1 });

        var result = await d.SelectRobotAsync(new Trigger { TargetRobotTags = "x" }, default);

        Assert.Equal(moreFree.Id, result!.Id);
    }

    [Fact]
    public async Task SelectRobot_EmptyTargetTags_MatchesAnyOnline()
    {
        var any = Rbt("A", "", RobotStatus.Online, 1, DateTime.UtcNow);
        var d = Build(new[] { any }, new Dictionary<Guid, int>());

        var result = await d.SelectRobotAsync(new Trigger { TargetRobotTags = "" }, default);

        Assert.Equal(any.Id, result!.Id);
    }
}

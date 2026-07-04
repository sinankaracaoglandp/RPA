namespace RPA.Domain.Tests;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using Xunit;

public class EntityTests
{
    [Fact]
    public void Project_CreateNew_ShouldHaveValidId()
    {
        var project = new Project { Name = "Test Project" };
        Assert.NotEqual(Guid.Empty, project.Id);
        Assert.Equal("Test Project", project.Name);
    }

    [Fact]
    public void Workflow_CreateNew_ShouldBeDraft()
    {
        var workflow = new Workflow { Name = "Test Workflow" };
        var version = new WorkflowVersion { Version = "1.0.0", Status = ComponentStatus.Draft };
        Assert.Equal(ComponentStatus.Draft, version.Status);
    }

    [Fact]
    public void QueueItem_CreateNew_ShouldBeNew()
    {
        var item = new QueueItem { IdempotencyKey = "key1", Status = QueueItemStatus.New };
        Assert.Equal(QueueItemStatus.New, item.Status);
        Assert.Equal("key1", item.IdempotencyKey);
    }

    [Fact]
    public void Robot_CreateNew_ShouldBeOffline()
    {
        var robot = new Robot { MachineName = "ROBOT-01", Mode = RobotMode.Unattended, Status = RobotStatus.Offline };
        Assert.Equal(RobotStatus.Offline, robot.Status);
    }
}

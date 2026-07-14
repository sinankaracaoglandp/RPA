using RPA.Domain.Entities;
using RPA.Domain.Enums;
using Xunit;

namespace RPA.Domain.Tests;

public class TriggerTests
{
    [Fact]
    public void Trigger_HasRobotTargetingDefaults()
    {
        var trigger = new Trigger();
        Assert.Equal("", trigger.TargetRobotTags);
        Assert.Equal(0, trigger.Priority);
    }

    [Fact]
    public void Trigger_CanSetRobotTargeting()
    {
        var trigger = new Trigger { TargetRobotTags = "prod-vm,sap", Priority = 5, Type = TriggerType.Cron };
        Assert.Equal("prod-vm,sap", trigger.TargetRobotTags);
        Assert.Equal(5, trigger.Priority);
    }
}

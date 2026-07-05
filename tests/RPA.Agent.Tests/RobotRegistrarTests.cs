namespace RPA.Agent.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RPA.Agent.Configuration;
using RPA.Agent.Registration;
using RPA.Agent.State;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;

public class RobotRegistrarTests
{
    [Fact]
    public async Task Kayit_RobotId_Durum_Icine_Yazar()
    {
        var robot = new Robot { Id = Guid.NewGuid(), MachineName = "RBT-01" };
        var svc = new Mock<IRobotService>();
        RobotRegistrationRequest? sent = null;
        svc.Setup(s => s.RegisterAsync(It.IsAny<RobotRegistrationRequest>(), It.IsAny<CancellationToken>()))
            .Callback<RobotRegistrationRequest, CancellationToken>((r, _) => sent = r)
            .ReturnsAsync(robot);
        var state = new AgentState();
        var options = Options.Create(new AgentOptions { MachineName = "RBT-01", Mode = RobotMode.Attended, Tags = "sap", Capacity = 2 });
        var registrar = new RobotRegistrar(svc.Object, state, options, NullLogger<RobotRegistrar>.Instance);

        var id = await registrar.RegisterAsync();

        Assert.Equal(robot.Id, id);
        Assert.Equal(robot.Id, state.RobotId);
        Assert.Equal(AgentActivity.Idle, state.Activity);
        Assert.NotNull(sent);
        Assert.Equal("RBT-01", sent!.MachineName);
        Assert.Equal(RobotMode.Attended, sent.Mode);
        Assert.Equal("sap", sent.Tags);
        Assert.Equal(2, sent.Capacity);
    }

    [Fact]
    public async Task MachineName_Bossa_Ortam_Adi_Kullanilir()
    {
        var svc = new Mock<IRobotService>();
        RobotRegistrationRequest? sent = null;
        svc.Setup(s => s.RegisterAsync(It.IsAny<RobotRegistrationRequest>(), It.IsAny<CancellationToken>()))
            .Callback<RobotRegistrationRequest, CancellationToken>((r, _) => sent = r)
            .ReturnsAsync(new Robot { Id = Guid.NewGuid() });
        var registrar = new RobotRegistrar(svc.Object, new AgentState(),
            Options.Create(new AgentOptions { MachineName = "" }), NullLogger<RobotRegistrar>.Instance);

        await registrar.RegisterAsync();

        Assert.Equal(System.Environment.MachineName, sent!.MachineName);
    }
}

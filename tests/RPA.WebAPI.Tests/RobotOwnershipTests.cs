namespace RPA.WebAPI.Tests;

using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Infrastructure.Authentication;
using RPA.Infrastructure.Persistence;

/// <summary>
/// Review bulgusu (IDOR): RobotHub.Register/Heartbeat kimligi dogruluyor ama NESNE duzeyinde
/// yetkilendirme yapmiyordu â€” istemcinin gonderdigi robotId ile token'daki agent_id arasinda
/// bag yoktu. Sonuc: aktive edilmis herhangi bir ajan baska bir robotun grubuna kaydolup ona
/// atanan isleri alabilir, ya da onun adina heartbeat atip Online gosterebilirdi.
///
/// Bu testler GERCEK RobotService + gercek EF uzerinden kosar (mock robot servisi yok):
/// olculen sey, sahiplik baginin veride gercekten kurulup zorlandigidir.
/// </summary>
public sealed class RobotOwnershipTests
{
    [Fact]
    public async Task Register_BindsRobotToAuthenticatedAgent_NotToClientSuppliedData()
    {
        var agentId = Guid.NewGuid();
        await using var app = await LicensedTestApp.CreateAsync(realRobotService: true);
        await app.AddAgentAsync(agentId, AgentIdentityStatus.Activated, "hash");
        await using var connection = Connect(app, agentId);
        await connection.StartAsync();

        await connection.InvokeAsync("Register", new { MachineName = "RPA-01", Mode = "Unattended", Capacity = 1 });

        using var scope = app.Factory.Services.CreateScope();
        var robot = await scope.ServiceProvider.GetRequiredService<RpaDbContext>()
            .Robots.SingleAsync(x => x.MachineName == "RPA-01");
        Assert.Equal(agentId, robot.AgentIdentityId);
    }

    [Fact]
    public async Task Heartbeat_ForRobotOwnedByAnotherAgent_IsRejected()
    {
        var owner = Guid.NewGuid();
        var attacker = Guid.NewGuid();
        await using var app = await LicensedTestApp.CreateAsync(realRobotService: true);
        await app.AddAgentAsync(owner, AgentIdentityStatus.Activated, "hash-owner");
        await app.AddAgentAsync(attacker, AgentIdentityStatus.Activated, "hash-attacker");

        // Kurban ajan kendi robotunu kaydeder.
        await using (var ownerConnection = Connect(app, owner))
        {
            await ownerConnection.StartAsync();
            await ownerConnection.InvokeAsync("Register", new { MachineName = "RPA-OWNER", Mode = "Unattended", Capacity = 1 });
        }

        Guid victimRobotId;
        using (var scope = app.Factory.Services.CreateScope())
        {
            victimRobotId = (await scope.ServiceProvider.GetRequiredService<RpaDbContext>()
                .Robots.SingleAsync(x => x.MachineName == "RPA-OWNER")).Id;
        }

        // Saldirgan ajan, kurbanin robotId'siyle heartbeat atmayi dener.
        await using var attackerConnection = Connect(app, attacker);
        await attackerConnection.StartAsync();

        var error = await Assert.ThrowsAsync<HubException>(() =>
            attackerConnection.InvokeAsync("Heartbeat", victimRobotId));
        Assert.Contains("ROBOT_NOT_OWNED", error.Message);
    }

    [Fact]
    public async Task Register_ForMachineOwnedByAnotherAgent_IsRejected()
    {
        var owner = Guid.NewGuid();
        var attacker = Guid.NewGuid();
        await using var app = await LicensedTestApp.CreateAsync(realRobotService: true);
        await app.AddAgentAsync(owner, AgentIdentityStatus.Activated, "hash-owner");
        await app.AddAgentAsync(attacker, AgentIdentityStatus.Activated, "hash-attacker");

        await using (var ownerConnection = Connect(app, owner))
        {
            await ownerConnection.StartAsync();
            await ownerConnection.InvokeAsync("Register", new { MachineName = "RPA-SHARED", Mode = "Unattended", Capacity = 1 });
        }

        // Saldirgan ayni makine adiyla kaydolup robotu devralmayi dener (makine adi idempotent anahtardir).
        await using var attackerConnection = Connect(app, attacker);
        await attackerConnection.StartAsync();

        var error = await Assert.ThrowsAsync<HubException>(() =>
            attackerConnection.InvokeAsync("Register", new { MachineName = "RPA-SHARED", Mode = "Unattended", Capacity = 1 }));
        Assert.Contains("ROBOT_NOT_OWNED", error.Message);

        using var scope = app.Factory.Services.CreateScope();
        var robot = await scope.ServiceProvider.GetRequiredService<RpaDbContext>()
            .Robots.SingleAsync(x => x.MachineName == "RPA-SHARED");
        Assert.Equal(owner, robot.AgentIdentityId);
    }

    [Fact]
    public async Task Heartbeat_ForOwnRobot_Succeeds()
    {
        var agentId = Guid.NewGuid();
        await using var app = await LicensedTestApp.CreateAsync(realRobotService: true);
        await app.AddAgentAsync(agentId, AgentIdentityStatus.Activated, "hash");
        await using var connection = Connect(app, agentId);
        await connection.StartAsync();
        await connection.InvokeAsync("Register", new { MachineName = "RPA-OWN", Mode = "Unattended", Capacity = 1 });

        Guid robotId;
        using (var scope = app.Factory.Services.CreateScope())
        {
            robotId = (await scope.ServiceProvider.GetRequiredService<RpaDbContext>()
                .Robots.SingleAsync(x => x.MachineName == "RPA-OWN")).Id;
        }

        await connection.InvokeAsync("Heartbeat", robotId);

        using var verify = app.Factory.Services.CreateScope();
        var robot = await verify.ServiceProvider.GetRequiredService<RpaDbContext>()
            .Robots.SingleAsync(x => x.Id == robotId);
        Assert.Equal(RobotStatus.Online, robot.Status);
    }

    private static HubConnection Connect(LicensedTestApp app, Guid agentId)
    {
        var server = app.Factory.Server;
        var token = AgentToken(app, agentId);
        return new HubConnectionBuilder()
            .WithUrl($"{server.BaseAddress}hubs/robot", options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();
    }

    private static string AgentToken(LicensedTestApp app, Guid agentId)
    {
        using var scope = app.Factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<AuthenticationOptions>>();
        return new AgentTokenService(options).GenerateAccessToken(new AgentIdentity
        {
            Id = agentId,
            LicenseInstallationId = app.InstallationRowId,
            Status = AgentIdentityStatus.Activated,
        });
    }
}


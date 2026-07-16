namespace RPA.Agent.Tests.Connectivity;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RPA.Agent;
using RPA.Agent.Configuration;
using RPA.Agent.Connectivity;
using RPA.Agent.Hosting;
using RPA.Agent.State;
using RPA.Domain.Entities;
using RPA.Domain.Exceptions;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Workflow;

/// <summary>
/// Task 10 — Task 6'nin BAGLANMAMIS birakigi kablolama: kira + kapi DI'da olmali ve kira
/// gercek bir sunucu dogrulamasi ile beslenmelidir. Kablolanmadan once
/// <see cref="ConnectivityLeaseContinuationGate"/> hicbir yerde olusturulmuyordu → 15 dakikalik
/// offline siniri URETIMDE HIC UYGULANMIYORDU (yalniz birim testlerinde vardi).
/// </summary>
public class ConnectivityLeaseWiringTests
{
    private static ServiceProvider BuildAgentServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAgentCore(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:OrchestratorUrl"] = "http://localhost:5000",
                ["Agent:Mode"] = "Unattended",
            })
            .Build());
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddAgentCore_RegistersLeaseAndContinuationGate()
    {
        using var provider = BuildAgentServices();

        var lease = provider.GetService<ConnectivityLease>();
        var gate = provider.GetService<IExecutionContinuationGate>();

        Assert.NotNull(lease);
        Assert.IsType<ConnectivityLeaseContinuationGate>(gate);
        // Kira TEKILDIR: tum tuketiciler ayni kirayi gormeli (aksi halde her scope kendi
        // 15 dakikasini yeniden baslatirdi ve sinir hicbir zaman dolmazdi).
        Assert.Same(lease, provider.GetRequiredService<ConnectivityLease>());
    }

    /// <summary>
    /// Kablolamanin ASIL kaniti: ajanin DI konteynerinden cozulen GERCEK IWorkflowRunner
    /// (BaseRunner), kira dolunca sonraki node'u baslatmaz. Bu test, kapinin BaseRunner'in
    /// opsiyonel parametresine gercekten aktigini davranisla dogrular.
    /// </summary>
    [Fact]
    public async Task ResolvedWorkflowRunner_SuspendsAtNodeBoundary_WhenLeaseExpired()
    {
        const string twoNodeWorkflow = """
        {
          "schemaVersion": "1.0",
          "id": "44444444-4444-4444-4444-444444444444",
          "name": "Kira kablolamasi",
          "version": "1.0.0",
          "arguments": { "in": [], "out": [ { "name": "message", "type": "string" } ] },
          "nodes": [
            { "id": "n1", "type": "assign", "variableName": "message", "value": "bir" },
            { "id": "n2", "type": "assign", "variableName": "message", "value": "iki" }
          ],
          "connections": [ { "from": "n1", "to": "n2" } ]
        }
        """;

        var clock = new TestClock(DateTimeOffset.UnixEpoch);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWorkflowServices();
        services.AddAgentCore(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:OrchestratorUrl"] = "http://localhost:5000",
                ["Agent:Mode"] = "Unattended",
            })
            .Build());
        // Sahte saatli kira ile degistir (AddAgentCore'un kaydi TimeProvider.System kullanir).
        services.AddSingleton(new ConnectivityLease(clock));
        await using var provider = services.BuildServiceProvider();

        var runner = provider.GetRequiredService<IWorkflowRunner>();
        clock.Advance(TimeSpan.FromMinutes(15)); // 15 dk boyunca basarili sunucu dogrulamasi yok.

        var result = await runner.ExecuteAsync(
            new WorkflowVersion { JsonDefinition = twoNodeWorkflow }, new(), Guid.NewGuid());

        Assert.False(result.Success);
        Assert.IsType<ExecutionSuspendedException>(result.Exception);
    }

    [Fact]
    public async Task Heartbeat_Success_RenewsLease()
    {
        var clock = new TestClock(DateTimeOffset.UnixEpoch);
        var lease = new ConnectivityLease(clock);
        var service = Heartbeat(lease, heartbeatSucceeds: true);

        clock.Advance(TimeSpan.FromMinutes(10));
        await service.SendHeartbeatAsync();
        clock.Advance(TimeSpan.FromMinutes(10)); // Ilk kiradan 20 dk; yenilenen kiradan 10 dk.

        Assert.True(lease.IsValid);
        Assert.True(lease.IsConnected);
    }

    [Fact]
    public async Task Heartbeat_Failure_MarksDisconnectedWithoutRenewingLease()
    {
        var clock = new TestClock(DateTimeOffset.UnixEpoch);
        var lease = new ConnectivityLease(clock);
        var service = Heartbeat(lease, heartbeatSucceeds: false);

        clock.Advance(TimeSpan.FromMinutes(10));
        await service.SendHeartbeatAsync();

        Assert.False(lease.IsConnected);
        Assert.True(lease.IsValid); // Kopma kirayi ANINDA gecersizlestirmez.

        clock.Advance(TimeSpan.FromMinutes(5)); // Son BASARILI dogrulamadan 15 dk.
        Assert.False(lease.IsValid);
    }

    private static HeartbeatBackgroundService Heartbeat(ConnectivityLease lease, bool heartbeatSucceeds)
    {
        var robotService = new Mock<IRobotService>();
        var setup = robotService.Setup(r => r.RecordHeartbeatAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()));
        if (heartbeatSucceeds) setup.ReturnsAsync(new Robot());
        else setup.ThrowsAsync(new HttpRequestException("orkestrator erisilemiyor"));

        var services = new ServiceCollection();
        services.AddScoped(_ => robotService.Object);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var state = new AgentState();
        state.SetRobotId(Guid.NewGuid());
        return new HeartbeatBackgroundService(scopeFactory, state, Options.Create(new AgentOptions()),
            NullLogger<HeartbeatBackgroundService>.Instance, lease);
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now;
        public TestClock(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now += delta;
    }
}

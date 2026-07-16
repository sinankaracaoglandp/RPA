namespace RPA.Agent.Tests;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RPA.Agent.Configuration;
using RPA.Agent.Hosting;
using RPA.Agent.Jobs;
using RPA.Agent.Registration;
using RPA.Agent.State;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Retry;

public class HostedServiceTests
{
    private static IServiceScopeFactory ScopeFactory(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    // --- RegistrationHostedService ---

    [Fact]
    public async Task Registration_StartAsync_Registrar_Cagirir()
    {
        var registrar = new Mock<IRobotRegistrar>();
        registrar.Setup(r => r.RegisterAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Guid.NewGuid());
        var sf = ScopeFactory(s => s.AddScoped(_ => registrar.Object));
        var session = new Mock<ISessionManager>();
        var svc = new RegistrationHostedService(sf, session.Object,
            Options.Create(new AgentOptions { Mode = RobotMode.Unattended }),
            NullLogger<RegistrationHostedService>.Instance);

        await svc.StartAsync(CancellationToken.None);

        registrar.Verify(r => r.RegisterAsync(It.IsAny<CancellationToken>()), Times.Once);
        session.Verify(s => s.EnsureSessionAsync(SessionMode.Unattended, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Registration_Attended_Mod_EnsureSession_Attended_Cagirir()
    {
        var registrar = new Mock<IRobotRegistrar>();
        registrar.Setup(r => r.RegisterAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Guid.NewGuid());
        var sf = ScopeFactory(s => s.AddScoped(_ => registrar.Object));
        var session = new Mock<ISessionManager>();
        var svc = new RegistrationHostedService(sf, session.Object,
            Options.Create(new AgentOptions { Mode = RobotMode.Attended }),
            NullLogger<RegistrationHostedService>.Instance);

        await svc.StartAsync(CancellationToken.None);

        session.Verify(s => s.EnsureSessionAsync(SessionMode.Attended, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Registration_Oturum_Hatasi_Yutulur()
    {
        var registrar = new Mock<IRobotRegistrar>();
        registrar.Setup(r => r.RegisterAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Guid.NewGuid());
        var sf = ScopeFactory(s => s.AddScoped(_ => registrar.Object));
        var session = new Mock<ISessionManager>();
        session.Setup(s => s.EnsureSessionAsync(It.IsAny<SessionMode>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("AutoLogon devre dışı"));
        var svc = new RegistrationHostedService(sf, session.Object,
            Options.Create(new AgentOptions { Mode = RobotMode.Unattended }),
            NullLogger<RegistrationHostedService>.Instance);

        await svc.StartAsync(CancellationToken.None); // Fırlatmamalı.

        registrar.Verify(r => r.RegisterAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // --- HeartbeatBackgroundService ---

    [Fact]
    public async Task Heartbeat_RobotId_Yoksa_Cagirmaz()
    {
        var robotSvc = new Mock<IRobotService>();
        var sf = ScopeFactory(s => s.AddScoped(_ => robotSvc.Object));
        var svc = new HeartbeatBackgroundService(sf, new AgentState(),
            Options.Create(new AgentOptions()), NullLogger<HeartbeatBackgroundService>.Instance);

        await svc.SendHeartbeatAsync();

        robotSvc.Verify(r => r.RecordHeartbeatAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Heartbeat_RobotId_Varsa_Kaydeder_Ve_Zaman_Damgalar()
    {
        var robotSvc = new Mock<IRobotService>();
        robotSvc.Setup(r => r.RecordHeartbeatAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Robot());
        var state = new AgentState();
        var robotId = Guid.NewGuid();
        state.SetRobotId(robotId);
        var sf = ScopeFactory(s => s.AddScoped(_ => robotSvc.Object));
        var svc = new HeartbeatBackgroundService(sf, state,
            Options.Create(new AgentOptions()), NullLogger<HeartbeatBackgroundService>.Instance);

        await svc.SendHeartbeatAsync();

        robotSvc.Verify(r => r.RecordHeartbeatAsync(robotId, It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(state.LastHeartbeatUtc);
    }

    [Fact]
    public async Task Heartbeat_Hata_Yutulur_Dongu_Devam_Eder()
    {
        var robotSvc = new Mock<IRobotService>();
        robotSvc.Setup(r => r.RecordHeartbeatAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("orchestrator erişilemez"));
        var state = new AgentState();
        state.SetRobotId(Guid.NewGuid());
        var sf = ScopeFactory(s => s.AddScoped(_ => robotSvc.Object));
        var svc = new HeartbeatBackgroundService(sf, state,
            Options.Create(new AgentOptions()), NullLogger<HeartbeatBackgroundService>.Instance);

        await svc.SendHeartbeatAsync(); // Fırlatmamalı.
    }

    // --- QueuePollingBackgroundService ---

    private static IServiceScopeFactory PollScope(Mock<IAgentJobSource> source, Mock<IWorkflowRunner> runner, AgentState state)
        => ScopeFactory(s =>
        {
            s.AddScoped(_ => source.Object);
            s.AddSingleton(state);
            s.AddSingleton<IAgentState>(state);
            s.AddSingleton<ExceptionClassifier>();
            s.AddScoped(_ => new JobExecutor(runner.Object, new ExceptionClassifier(), state, NullLogger<JobExecutor>.Instance));
        });

    private static QueuePollingBackgroundService MakePolling(IServiceScopeFactory sf, AgentState state)
        => new(sf, state, Options.Create(new AgentOptions()), NullLogger<QueuePollingBackgroundService>.Instance);

    [Fact]
    public async Task Poll_Is_Yoksa_False_Doner()
    {
        var source = new Mock<IAgentJobSource>();
        source.Setup(s => s.DequeueAsync(It.IsAny<CancellationToken>())).ReturnsAsync((AgentJob?)null);
        var state = new AgentState();
        state.SetRobotId(Guid.NewGuid());
        var svc = MakePolling(PollScope(source, new Mock<IWorkflowRunner>(), state), state);

        Assert.False(await svc.PollOnceAsync());
    }

    [Fact]
    public async Task Poll_Duraklatilmissa_Is_Cekmez()
    {
        var source = new Mock<IAgentJobSource>();
        var state = new AgentState();
        state.SetRobotId(Guid.NewGuid());
        state.SetPaused(true);
        var svc = MakePolling(PollScope(source, new Mock<IWorkflowRunner>(), state), state);

        Assert.False(await svc.PollOnceAsync());
        source.Verify(s => s.DequeueAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Poll_Basarili_Is_ReportSuccess_Cagirir()
    {
        var itemId = Guid.NewGuid();
        var source = new Mock<IAgentJobSource>();
        source.Setup(s => s.DequeueAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentJob(itemId, new WorkflowVersion { Id = Guid.NewGuid() }, new()));
        var runner = new Mock<IWorkflowRunner>();
        runner.Setup(r => r.ExecuteAsync(It.IsAny<WorkflowVersion>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowExecutionResult { Success = true });
        var state = new AgentState();
        state.SetRobotId(Guid.NewGuid());
        var svc = MakePolling(PollScope(source, runner, state), state);

        Assert.True(await svc.PollOnceAsync());
        source.Verify(s => s.ReportSuccessAsync(itemId, It.IsAny<CancellationToken>()), Times.Once);
        source.Verify(s => s.ReportFailureAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Poll_Basarisiz_Is_ReportFailure_Business_Bayragiyla_Cagirir()
    {
        var itemId = Guid.NewGuid();
        var source = new Mock<IAgentJobSource>();
        source.Setup(s => s.DequeueAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentJob(itemId, new WorkflowVersion { Id = Guid.NewGuid() }, new()));
        var runner = new Mock<IWorkflowRunner>();
        runner.Setup(r => r.ExecuteAsync(It.IsAny<WorkflowVersion>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RPA.Domain.Exceptions.BusinessException("kural ihlali"));
        var state = new AgentState();
        state.SetRobotId(Guid.NewGuid());
        var svc = MakePolling(PollScope(source, runner, state), state);

        Assert.True(await svc.PollOnceAsync());
        source.Verify(s => s.ReportFailureAsync(itemId, It.IsAny<string>(), true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Poll_QueueName_Ile_Cozulen_StudioRun_Isini_Runnera_Tasir()
    {
        var queueId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var workflowVersionId = Guid.NewGuid();
        var robotId = Guid.NewGuid();
        var payload = $$"""
            {
              "workflowVersionId": "{{workflowVersionId}}",
              "version": "1.0.0",
              "jsonDefinition": { "schemaVersion": "1.0", "nodes": [], "connections": [] },
              "arguments": { "customer": "ACME" }
            }
            """;
        var queue = new Mock<IQueueService>();
        queue.Setup(q => q.ListQueuesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new QueueSummary(queueId, "StudioRun", 0, null, 1, 0, 0, 1),
            });
        queue.Setup(q => q.GetNextItemAsync(queueId, robotId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueueItem { Id = itemId, QueueId = queueId, Payload = payload });
        queue.Setup(q => q.CompleteAsync(itemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueueItem { Id = itemId, QueueId = queueId, Status = QueueItemStatus.Successful });

        var runner = new Mock<IWorkflowRunner>();
        runner.Setup(r => r.ExecuteAsync(
                It.IsAny<WorkflowVersion>(),
                It.IsAny<Dictionary<string, object?>>(),
                itemId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowExecutionResult { Success = true });

        var state = new AgentState();
        state.SetRobotId(robotId);
        var sf = ScopeFactory(s =>
        {
            s.AddScoped(_ => queue.Object);
            s.AddScoped(_ => runner.Object);
            s.AddSingleton(state);
            s.AddSingleton<IAgentState>(state);
            s.AddSingleton<ExceptionClassifier>();
            s.AddSingleton(Options.Create(new AgentOptions { QueueName = "StudioRun" }));
            s.AddSingleton<Microsoft.Extensions.Logging.ILogger<QueueAgentJobSource>>(
                NullLogger<QueueAgentJobSource>.Instance);
            s.AddScoped<IAgentJobSource, QueueAgentJobSource>();
            s.AddScoped(sp => new JobExecutor(
                sp.GetRequiredService<IWorkflowRunner>(),
                sp.GetRequiredService<ExceptionClassifier>(),
                sp.GetRequiredService<IAgentState>(),
                NullLogger<JobExecutor>.Instance));
        });
        var svc = new QueuePollingBackgroundService(
            sf,
            state,
            Options.Create(new AgentOptions { QueueName = "StudioRun" }),
            NullLogger<QueuePollingBackgroundService>.Instance);

        Assert.True(await svc.PollOnceAsync());
        runner.Verify(r => r.ExecuteAsync(
            It.Is<WorkflowVersion>(v =>
                v.Id == workflowVersionId &&
                v.JsonDefinition.Contains("schemaVersion")),
            It.Is<Dictionary<string, object?>>(a => (string?)a["customer"] == "ACME"),
            itemId,
            It.IsAny<CancellationToken>()), Times.Once);
        queue.Verify(q => q.CompleteAsync(itemId, It.IsAny<CancellationToken>()), Times.Once);
    }
}

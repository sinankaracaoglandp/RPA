namespace RPA.Infrastructure.Tests;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Scheduling;

/// <summary>
/// Task 3.3 — SchedulerHostedService: aktif Cron tetikleyicilerini tarayıp zamanı gelenleri
/// ateşlediğini doğrular (Spec Bölüm 7).
/// </summary>
public class SchedulerHostedServiceTests
{
    private static IServiceScopeFactory BuildScopeFactory(ITriggerRepository repository, ITriggerService triggerService)
    {
        var services = new ServiceCollection();
        services.AddSingleton(repository);
        services.AddSingleton(triggerService);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static Trigger MakeTrigger(bool active = true) => new()
    {
        Id = Guid.NewGuid(),
        ProjectId = Guid.NewGuid(),
        WorkflowVersionId = Guid.NewGuid(),
        Type = TriggerType.Cron,
        EnvironmentId = Guid.NewGuid(),
        IsActive = active,
        CreatedAt = DateTime.UtcNow.AddDays(-1),
    };

    [Fact]
    public async Task ScanOnce_DueSchedule_ExecutesTrigger()
    {
        var trigger = MakeTrigger();
        var schedule = new Schedule { TriggerId = trigger.Id, CronExpression = "* * * * *", TimeZone = "UTC", OverlapPolicy = "parallel" };

        var repo = new Mock<ITriggerRepository>();
        repo.Setup(r => r.FindActiveCronTriggersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Trigger> { trigger });
        repo.Setup(r => r.FindScheduleByTriggerIdAsync(trigger.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(schedule);

        var triggerService = new Mock<ITriggerService>();
        triggerService.Setup(s => s.ExecuteTriggerAsync(trigger.Id, "cron", It.IsAny<CancellationToken>()))
            .ReturnsAsync(TriggerExecutionResult.Executed(new JobRun()));

        var scheduler = new SchedulerHostedService(
            BuildScopeFactory(repo.Object, triggerService.Object),
            new MockLogger<SchedulerHostedService>());

        await scheduler.ScanOnceAsync();

        triggerService.Verify(s => s.ExecuteTriggerAsync(trigger.Id, "cron", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ScanOnce_NotYetDue_DoesNotExecute()
    {
        var trigger = MakeTrigger();
        // Cron: her gün 09:00. CreatedAt (lastFire) az önce -> muhtemelen henüz zamanı gelmedi.
        trigger.CreatedAt = DateTime.UtcNow;
        var schedule = new Schedule { TriggerId = trigger.Id, CronExpression = "0 9 * * *", TimeZone = "UTC", OverlapPolicy = "parallel" };

        var repo = new Mock<ITriggerRepository>();
        repo.Setup(r => r.FindActiveCronTriggersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Trigger> { trigger });
        repo.Setup(r => r.FindScheduleByTriggerIdAsync(trigger.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(schedule);

        var triggerService = new Mock<ITriggerService>();

        var scheduler = new SchedulerHostedService(
            BuildScopeFactory(repo.Object, triggerService.Object),
            new MockLogger<SchedulerHostedService>());

        // "0 9 * * *" bir sonraki oluşumu şimdiden en az birkaç saat sonra olacağından tetiklenmemeli
        // (test her saatte flaky olmaması için: eğer test saat 08:59-09:00 arası koşarsa nadiren
        // farklı davranabilir; CronScheduleCalculator birim testleriyle ayrıca doğrulanmıştır).
        await scheduler.ScanOnceAsync();

        triggerService.Verify(s => s.ExecuteTriggerAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ScanOnce_TwiceInSamePeriod_ExecutesOnlyOnce()
    {
        var trigger = MakeTrigger();
        var schedule = new Schedule { TriggerId = trigger.Id, CronExpression = "* * * * *", TimeZone = "UTC", OverlapPolicy = "parallel" };

        var repo = new Mock<ITriggerRepository>();
        repo.Setup(r => r.FindActiveCronTriggersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Trigger> { trigger });
        repo.Setup(r => r.FindScheduleByTriggerIdAsync(trigger.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(schedule);

        var triggerService = new Mock<ITriggerService>();
        triggerService.Setup(s => s.ExecuteTriggerAsync(trigger.Id, "cron", It.IsAny<CancellationToken>()))
            .ReturnsAsync(TriggerExecutionResult.Executed(new JobRun()));

        var scheduler = new SchedulerHostedService(
            BuildScopeFactory(repo.Object, triggerService.Object),
            new MockLogger<SchedulerHostedService>());

        await scheduler.ScanOnceAsync();
        await scheduler.ScanOnceAsync();

        // İkinci tarama, bellek içi lastFire güncellendiği için (bir sonraki dakikaya kadar) tekrar ateşlememeli.
        triggerService.Verify(s => s.ExecuteTriggerAsync(trigger.Id, "cron", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ScanOnce_NoScheduleForTrigger_SkipsWithoutError()
    {
        var trigger = MakeTrigger();

        var repo = new Mock<ITriggerRepository>();
        repo.Setup(r => r.FindActiveCronTriggersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Trigger> { trigger });
        repo.Setup(r => r.FindScheduleByTriggerIdAsync(trigger.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Schedule?)null);

        var triggerService = new Mock<ITriggerService>();

        var scheduler = new SchedulerHostedService(
            BuildScopeFactory(repo.Object, triggerService.Object),
            new MockLogger<SchedulerHostedService>());

        await scheduler.ScanOnceAsync();

        triggerService.Verify(s => s.ExecuteTriggerAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StopAsync_DuringDelay_DoesNotWriteToConsole()
    {
        var repo = new Mock<ITriggerRepository>();
        repo.Setup(r => r.FindActiveCronTriggersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Trigger>());

        var scheduler = new SchedulerHostedService(
            BuildScopeFactory(repo.Object, Mock.Of<ITriggerService>()),
            new MockLogger<SchedulerHostedService>(),
            Options.Create(new SchedulerOptions { PollInterval = TimeSpan.FromHours(1) }));

        var originalOut = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);

        try
        {
            await scheduler.StartAsync(CancellationToken.None);
            await Task.Delay(50);

            await scheduler.StopAsync(CancellationToken.None);

            Assert.Equal(string.Empty, writer.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}

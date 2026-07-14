namespace RPA.Infrastructure.Tests;

using Microsoft.EntityFrameworkCore;
using Moq;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Persistence;
using RPA.Infrastructure.Scheduling;

/// <summary>
/// Task 3.3 — TriggerService: JobRun oluşturma ve OverlapPolicy (skip/queue/parallel) testleri
/// (Spec Bölüm 7).
/// </summary>
public class TriggerServiceTests
{
    private readonly Mock<IRobotDispatcher> _dispatcher = new();

    public TriggerServiceTests()
    {
        // Varsayılan: mevcut testlerin beklentileri değişmesin diye her zaman bir robot döner
        // (Running kalsın). Testler gerektiğinde bunu override eder.
        _dispatcher.Setup(d => d.SelectRobotAsync(It.IsAny<Trigger>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Robot { Id = Guid.NewGuid(), MachineName = "VM1", Status = RobotStatus.Online, Capacity = 1 });
    }

    private static RpaDbContext Db(string name)
    {
        var options = new DbContextOptionsBuilder<RpaDbContext>()
            .UseInMemoryDatabase(databaseName: name)
            .Options;
        var db = new RpaDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private sealed class FakeWorkflowRunner : IWorkflowRunner
    {
        public int CallCount;
        public bool ShouldFail;
        public TaskCompletionSource<bool>? Gate; // isteğe bağlı: eşzamanlılık senaryoları için bekletme

        public async Task<WorkflowExecutionResult> ExecuteAsync(
            WorkflowVersion workflowVersion, Dictionary<string, object?> arguments, Guid jobRunId, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref CallCount);
            if (Gate is not null)
                await Gate.Task;

            return new WorkflowExecutionResult { Success = !ShouldFail };
        }

        public Task<Dictionary<string, object?>> InvokeComponentAsync(ComponentVersion componentVersion, Dictionary<string, object?> inputs, Guid jobRunId, CancellationToken cancellationToken = default)
            => Task.FromResult(new Dictionary<string, object?>());

        public Task<WorkflowExecutionResult> ResumeAsync(WorkflowVersion workflowVersion, Dictionary<string, object?> arguments, string checkpointData, Guid jobRunId, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkflowExecutionResult { Success = true });
    }

    private static async Task<Trigger> SeedTrigger(RpaDbContext db, TriggerType type = TriggerType.Manual)
    {
        var trigger = new Trigger
        {
            ProjectId = Guid.NewGuid(),
            WorkflowVersionId = Guid.NewGuid(),
            Type = type,
            EnvironmentId = Guid.NewGuid(),
            IsActive = true,
        };
        db.Triggers.Add(trigger);
        await db.SaveChangesAsync();
        return trigger;
    }

    private static async Task<Schedule> SeedSchedule(RpaDbContext db, Guid triggerId, string overlapPolicy)
    {
        var schedule = new Schedule
        {
            TriggerId = triggerId,
            CronExpression = "0 9 * * *",
            TimeZone = "UTC",
            OverlapPolicy = overlapPolicy,
        };
        db.Schedules.Add(schedule);
        await db.SaveChangesAsync();
        return schedule;
    }

    private TriggerService Service(RpaDbContext db, FakeWorkflowRunner runner)
        => new(new EfTriggerRepository(db), runner, _dispatcher.Object, new MockLogger<TriggerService>());

    [Fact]
    public async Task ExecuteTrigger_UnknownTrigger_ReturnsNotFound()
    {
        using var db = Db(Guid.NewGuid().ToString());
        var result = await Service(db, new FakeWorkflowRunner()).ExecuteTriggerAsync(Guid.NewGuid(), "manual");
        Assert.Equal(TriggerExecutionOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task ExecuteTrigger_InactiveTrigger_ReturnsInactive()
    {
        using var db = Db(Guid.NewGuid().ToString());
        var trigger = await SeedTrigger(db);
        trigger.IsActive = false;
        await db.SaveChangesAsync();

        var result = await Service(db, new FakeWorkflowRunner()).ExecuteTriggerAsync(trigger.Id, "manual");
        Assert.Equal(TriggerExecutionOutcome.Inactive, result.Outcome);
    }

    [Fact]
    public async Task ExecuteTrigger_Manual_CreatesJobRunAndExecutes()
    {
        using var db = Db(Guid.NewGuid().ToString());
        var trigger = await SeedTrigger(db);
        var runner = new FakeWorkflowRunner();

        var result = await Service(db, runner).ExecuteTriggerAsync(trigger.Id, "manual");

        Assert.Equal(TriggerExecutionOutcome.Executed, result.Outcome);
        Assert.NotNull(result.JobRun);
        Assert.Equal(1, runner.CallCount);
        Assert.Equal("Successful", result.JobRun!.Status);
        Assert.NotNull(result.JobRun.CompletedAt);
        Assert.NotEmpty(result.JobRun.ElasticsearchCorrelationId);
    }

    [Fact]
    public async Task ExecuteTrigger_FailedExecution_SetsFailedStatus()
    {
        using var db = Db(Guid.NewGuid().ToString());
        var trigger = await SeedTrigger(db);
        var runner = new FakeWorkflowRunner { ShouldFail = true };

        var result = await Service(db, runner).ExecuteTriggerAsync(trigger.Id, "manual");

        Assert.Equal("Failed", result.JobRun!.Status);
    }

    [Fact]
    public async Task OverlapPolicy_Skip_PreventsConcurrentRun()
    {
        using var db = Db(Guid.NewGuid().ToString());
        var trigger = await SeedTrigger(db, TriggerType.Cron);
        await SeedSchedule(db, trigger.Id, "skip");

        // Halihazırda çalışan bir JobRun simüle edilir.
        db.JobRuns.Add(new JobRun { WorkflowVersionId = trigger.WorkflowVersionId, Status = "Running", TriggeredBy = "cron", StartedAt = DateTime.UtcNow, ElasticsearchCorrelationId = "x" });
        await db.SaveChangesAsync();

        var runner = new FakeWorkflowRunner();
        var result = await Service(db, runner).ExecuteTriggerAsync(trigger.Id, "cron");

        Assert.Equal(TriggerExecutionOutcome.Skipped, result.Outcome);
        Assert.Equal(0, runner.CallCount);
        // Yalnızca önceden var olan JobRun kaydı olmalı, yeni oluşturulmamalı.
        Assert.Equal(1, await db.JobRuns.CountAsync());
    }

    [Fact]
    public async Task OverlapPolicy_Queue_EnqueuesWaitingRun()
    {
        using var db = Db(Guid.NewGuid().ToString());
        var trigger = await SeedTrigger(db, TriggerType.Cron);
        await SeedSchedule(db, trigger.Id, "queue");

        db.JobRuns.Add(new JobRun { WorkflowVersionId = trigger.WorkflowVersionId, Status = "Running", TriggeredBy = "cron", StartedAt = DateTime.UtcNow, ElasticsearchCorrelationId = "x" });
        await db.SaveChangesAsync();

        var runner = new FakeWorkflowRunner();
        var result = await Service(db, runner).ExecuteTriggerAsync(trigger.Id, "cron");

        Assert.Equal(TriggerExecutionOutcome.Queued, result.Outcome);
        Assert.Equal("Queued", result.JobRun!.Status);
        Assert.Equal(0, runner.CallCount); // henüz çalıştırılmadı
        Assert.Equal(2, await db.JobRuns.CountAsync());
    }

    [Fact]
    public async Task OverlapPolicy_Queue_StartsWaitingRunAfterPreviousCompletes()
    {
        var dbName = Guid.NewGuid().ToString();
        Guid triggerId, workflowVersionId;
        using (var seedDb = Db(dbName))
        {
            var trigger = await SeedTrigger(seedDb, TriggerType.Cron);
            await SeedSchedule(seedDb, trigger.Id, "queue");
            triggerId = trigger.Id;
            workflowVersionId = trigger.WorkflowVersionId;
        }

        var runner = new FakeWorkflowRunner { Gate = new TaskCompletionSource<bool>() };

        // İlk tetikleme: kendi DbContext'i ile ateşlenir, WorkflowRunner Gate açılana kadar bekler
        // (Running durumda kalır) — böylece ikinci tetikleme OverlapPolicy=queue ile karşılaşır.
        using var db1 = Db(dbName);
        var task1 = Service(db1, runner).ExecuteTriggerAsync(triggerId, "cron");

        // İlk JobRun'ın Running olarak kalıcı hale gelmesini bekle.
        using (var pollDb = Db(dbName))
        {
            for (var i = 0; i < 100 && !await pollDb.JobRuns.AnyAsync(j => j.WorkflowVersionId == workflowVersionId); i++)
                await Task.Delay(10);
        }

        using var db2 = Db(dbName);
        var second = await Service(db2, runner).ExecuteTriggerAsync(triggerId, "cron");
        Assert.Equal(TriggerExecutionOutcome.Queued, second.Outcome);

        // İlk çalıştırmanın tamamlanmasına izin ver; bu da zincirleme olarak kuyruktaki JobRun'ı başlatmalı.
        runner.Gate.SetResult(true);
        var first = await task1;
        Assert.Equal(TriggerExecutionOutcome.Executed, first.Outcome);

        using var db3 = Db(dbName);
        JobRun? queuedRow = null;
        for (var i = 0; i < 100; i++)
        {
            queuedRow = await db3.JobRuns.FirstAsync(j => j.Id == second.JobRun!.Id);
            if (queuedRow.Status == "Successful") break;
            await Task.Delay(10);
        }

        Assert.Equal("Successful", queuedRow!.Status);
        Assert.Equal(2, runner.CallCount);
    }

    [Fact]
    public async Task OverlapPolicy_Parallel_FiresRegardlessOfRunning()
    {
        using var db = Db(Guid.NewGuid().ToString());
        var trigger = await SeedTrigger(db, TriggerType.Cron);
        await SeedSchedule(db, trigger.Id, "parallel");

        db.JobRuns.Add(new JobRun { WorkflowVersionId = trigger.WorkflowVersionId, Status = "Running", TriggeredBy = "cron", StartedAt = DateTime.UtcNow, ElasticsearchCorrelationId = "x" });
        await db.SaveChangesAsync();

        var runner = new FakeWorkflowRunner();
        var result = await Service(db, runner).ExecuteTriggerAsync(trigger.Id, "cron");

        Assert.Equal(TriggerExecutionOutcome.Executed, result.Outcome);
        Assert.Equal(1, runner.CallCount);
        Assert.Equal(2, await db.JobRuns.CountAsync());
    }

    [Fact]
    public async Task ExecuteTrigger_AssignsSelectedRobot()
    {
        using var db = Db(Guid.NewGuid().ToString());
        var trigger = await SeedTrigger(db);
        var runner = new FakeWorkflowRunner();

        var robot = new Robot { Id = Guid.NewGuid(), MachineName = "VM1", Status = RobotStatus.Online, Capacity = 1 };
        _dispatcher.Setup(d => d.SelectRobotAsync(It.IsAny<Trigger>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(robot);

        var result = await Service(db, runner).ExecuteTriggerAsync(trigger.Id, "manual");

        Assert.Equal(robot.Id, result.JobRun!.AssignedRobotId);
    }

    [Fact]
    public async Task ExecuteTrigger_NoRobot_JobRunPending()
    {
        using var db = Db(Guid.NewGuid().ToString());
        var trigger = await SeedTrigger(db);
        var runner = new FakeWorkflowRunner();

        _dispatcher.Setup(d => d.SelectRobotAsync(It.IsAny<Trigger>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync((Robot?)null);

        var result = await Service(db, runner).ExecuteTriggerAsync(trigger.Id, "manual");

        Assert.Equal("Pending", result.JobRun!.Status);
        Assert.Null(result.JobRun.AssignedRobotId);
    }
}

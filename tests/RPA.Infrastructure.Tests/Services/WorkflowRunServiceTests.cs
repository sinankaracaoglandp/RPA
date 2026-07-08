namespace RPA.Infrastructure.Tests.Services;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Infrastructure.Persistence;
using RPA.Infrastructure.Queues;
using RPA.Infrastructure.Services;
using Xunit;
using Environment = RPA.Domain.Entities.Environment;

public class WorkflowRunServiceTests
{
    private static RpaDbContext Db()
    {
        var options = new DbContextOptionsBuilder<RpaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new RpaDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task EnqueueDraftAsync_CreatesStudioRunQueueItemWithAgentPayload()
    {
        await using var db = Db();
        var project = new Project { Id = Guid.NewGuid(), Name = "Pilot" };
        var workflow = new Workflow { Id = Guid.NewGuid(), ProjectId = project.Id, Name = "Flow" };
        var env = new Environment { Id = Guid.NewGuid(), Name = "Dev" };
        var version = new WorkflowVersion
        {
            Id = Guid.NewGuid(),
            WorkflowId = workflow.Id,
            Version = "1.0.0",
            EnvironmentId = env.Id,
            Status = ComponentStatus.Draft,
            JsonDefinition = """
                {"schemaVersion":"1.0","id":"wf","name":"Flow","version":"1.0.0","nodes":[],"connections":[]}
                """,
        };
        db.Projects.Add(project);
        db.Workflows.Add(workflow);
        db.Environments.Add(env);
        db.WorkflowVersions.Add(version);
        await db.SaveChangesAsync();

        var result = await new WorkflowRunService(db).EnqueueDraftAsync(
            workflow.Id,
            new Dictionary<string, object?> { ["customer"] = "ACME" });

        var item = Assert.Single(db.QueueItems);
        var queue = Assert.Single(db.Queues);
        Assert.Equal(item.Id, result.QueueItemId);
        Assert.Equal(queue.Id, result.QueueId);
        Assert.Equal(WorkflowRunService.StudioRunQueueName, queue.Name);
        Assert.Equal(QueueItemStatus.New, item.Status);

        using var payload = JsonDocument.Parse(item.Payload);
        var root = payload.RootElement;
        Assert.Equal(version.Id, root.GetProperty("workflowVersionId").GetGuid());
        Assert.Equal(env.Id, root.GetProperty("environmentId").GetGuid());
        Assert.Equal("ACME", root.GetProperty("arguments").GetProperty("customer").GetString());
        Assert.Equal("1.0", root.GetProperty("jsonDefinition").GetProperty("schemaVersion").GetString());
    }

    [Fact]
    public async Task EnqueueDraftAsync_ReusesStudioRunQueueAcrossProjects()
    {
        await using var db = Db();
        var env = new Environment { Id = Guid.NewGuid(), Name = "Dev" };
        db.Environments.Add(env);

        var project1 = new Project { Id = Guid.NewGuid(), Name = "Pilot 1" };
        var workflow1 = new Workflow { Id = Guid.NewGuid(), ProjectId = project1.Id, Name = "Flow 1" };
        var version1 = Draft(workflow1.Id, env.Id);

        var project2 = new Project { Id = Guid.NewGuid(), Name = "Pilot 2" };
        var workflow2 = new Workflow { Id = Guid.NewGuid(), ProjectId = project2.Id, Name = "Flow 2" };
        var version2 = Draft(workflow2.Id, env.Id);

        db.Projects.AddRange(project1, project2);
        db.Workflows.AddRange(workflow1, workflow2);
        db.WorkflowVersions.AddRange(version1, version2);
        await db.SaveChangesAsync();

        var service = new WorkflowRunService(db);

        var first = await service.EnqueueDraftAsync(workflow1.Id);
        var second = await service.EnqueueDraftAsync(workflow2.Id);

        Assert.Equal(first.QueueId, second.QueueId);
        Assert.Single(db.Queues);
        Assert.Equal(2, db.QueueItems.Count());
    }

    [Fact]
    public async Task EnqueueDraftAsync_InvalidDraftJson_ThrowsBusinessExceptionAndDoesNotCreateQueueItem()
    {
        await using var db = Db();
        var project = new Project { Id = Guid.NewGuid(), Name = "Pilot" };
        var workflow = new Workflow { Id = Guid.NewGuid(), ProjectId = project.Id, Name = "Flow" };
        var env = new Environment { Id = Guid.NewGuid(), Name = "Dev" };
        var version = Draft(workflow.Id, env.Id);
        version.JsonDefinition = "{ bozuk";

        db.Projects.Add(project);
        db.Workflows.Add(workflow);
        db.Environments.Add(env);
        db.WorkflowVersions.Add(version);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<RPA.Domain.Exceptions.BusinessException>(
            () => new WorkflowRunService(db).EnqueueDraftAsync(workflow.Id));

        Assert.Contains("geçersiz JSON", ex.Message);
        Assert.Empty(db.QueueItems);
    }

    [Fact]
    public async Task EnqueuedStudioRunItem_CanBeClaimedAndCompletedByQueueService()
    {
        await using var db = Db();
        var project = new Project { Id = Guid.NewGuid(), Name = "Pilot" };
        var workflow = new Workflow { Id = Guid.NewGuid(), ProjectId = project.Id, Name = "Flow" };
        var env = new Environment { Id = Guid.NewGuid(), Name = "Dev" };
        var version = Draft(workflow.Id, env.Id);
        db.Projects.Add(project);
        db.Workflows.Add(workflow);
        db.Environments.Add(env);
        db.WorkflowVersions.Add(version);
        await db.SaveChangesAsync();

        var run = await new WorkflowRunService(db).EnqueueDraftAsync(workflow.Id);
        var queue = new QueueService(new EfQueueItemRepository(db), new MockLogger<QueueService>());
        var robotId = Guid.NewGuid();

        var claimed = await queue.GetNextItemAsync(run.QueueId, robotId);

        Assert.NotNull(claimed);
        Assert.Equal(run.QueueItemId, claimed!.Id);
        Assert.Equal(QueueItemStatus.InProgress, claimed.Status);
        Assert.Equal(robotId, claimed.AssignedRobotId);

        var completed = await queue.CompleteAsync(run.QueueItemId);

        Assert.NotNull(completed);
        Assert.Equal(QueueItemStatus.Successful, completed!.Status);
        Assert.NotNull(completed.CompletedAt);
    }

    private static WorkflowVersion Draft(Guid workflowId, Guid environmentId) => new()
    {
        Id = Guid.NewGuid(),
        WorkflowId = workflowId,
        Version = "1.0.0",
        EnvironmentId = environmentId,
        Status = ComponentStatus.Draft,
        JsonDefinition = """
            {"schemaVersion":"1.0","id":"wf","name":"Flow","version":"1.0.0","nodes":[],"connections":[]}
            """,
    };
}

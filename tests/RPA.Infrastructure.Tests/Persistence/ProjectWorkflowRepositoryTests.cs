namespace RPA.Infrastructure.Tests.Persistence;

using Microsoft.EntityFrameworkCore;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Infrastructure.Persistence;
using Xunit;

/// <summary>Paket B — proje/workflow kalıcılığı: EF repo davranışları (InMemory).</summary>
public class ProjectWorkflowRepositoryTests
{
    private static RpaDbContext NewDb() => new(
        new DbContextOptionsBuilder<RpaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task Projects_AddAndList_Roundtrips()
    {
        using var db = NewDb();
        var repo = new EfProjectRepository(db);

        await repo.AddAsync(new Project { Id = Guid.NewGuid(), Name = "Pilot" });
        await repo.SaveChangesAsync();

        var list = await repo.ListAsync();
        Assert.Single(list);
        Assert.Equal("Pilot", list[0].Name);
    }

    [Fact]
    public async Task Projects_CountWorkflows_CountsOnlyThatProject()
    {
        using var db = NewDb();
        var projects = new EfProjectRepository(db);
        var workflows = new EfWorkflowRepository(db);
        var p1 = Guid.NewGuid();
        var p2 = Guid.NewGuid();
        await projects.AddAsync(new Project { Id = p1, Name = "A" });
        await projects.AddAsync(new Project { Id = p2, Name = "B" });
        await workflows.AddAsync(new Workflow { Id = Guid.NewGuid(), ProjectId = p1, Name = "w1" });
        await workflows.AddAsync(new Workflow { Id = Guid.NewGuid(), ProjectId = p1, Name = "w2" });
        await workflows.AddAsync(new Workflow { Id = Guid.NewGuid(), ProjectId = p2, Name = "w3" });
        await projects.SaveChangesAsync();

        Assert.Equal(2, await projects.CountWorkflowsAsync(p1));
        Assert.Equal(1, await projects.CountWorkflowsAsync(p2));
    }

    [Fact]
    public async Task Workflows_FindDraft_ReturnsOnlyDraftStatus()
    {
        using var db = NewDb();
        var repo = new EfWorkflowRepository(db);
        var wf = Guid.NewGuid();
        await repo.AddVersionAsync(new WorkflowVersion
        {
            Id = Guid.NewGuid(), WorkflowId = wf, Version = "1.0.0",
            Status = ComponentStatus.Published, JsonDefinition = "{}",
        });
        await repo.AddVersionAsync(new WorkflowVersion
        {
            Id = Guid.NewGuid(), WorkflowId = wf, Version = "1.1.0",
            Status = ComponentStatus.Draft, JsonDefinition = "{\"draft\":true}",
        });
        await repo.SaveChangesAsync();

        var draft = await repo.FindDraftAsync(wf);

        Assert.NotNull(draft);
        Assert.Equal("1.1.0", draft!.Version);
        Assert.Equal(ComponentStatus.Draft, draft.Status);
    }

    [Fact]
    public async Task Workflows_ListByProject_OrdersByUpdatedAtDescending()
    {
        using var db = NewDb();
        var repo = new EfWorkflowRepository(db);
        var p = Guid.NewGuid();
        await repo.AddAsync(new Workflow { Id = Guid.NewGuid(), ProjectId = p, Name = "eski", UpdatedAt = DateTime.UtcNow.AddDays(-1) });
        await repo.AddAsync(new Workflow { Id = Guid.NewGuid(), ProjectId = p, Name = "yeni", UpdatedAt = DateTime.UtcNow });
        await repo.SaveChangesAsync();

        var list = await repo.ListByProjectAsync(p);

        Assert.Equal(new[] { "yeni", "eski" }, list.Select(w => w.Name).ToArray());
    }
}

namespace RPA.Infrastructure.Tests;

using Microsoft.EntityFrameworkCore;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Infrastructure.Persistence;
using RPA.Infrastructure.Services;
using BusinessException = RPA.Domain.Exceptions.BusinessException;
using Environment = RPA.Domain.Entities.Environment;

/// <summary>
/// WP-6.4 — Ortam yönetimi (Dev/Test/Prod) + workflow deployment governance
/// (Draft → Test → Published). Spec Bölüm 5.5, 9 (publish/approve).
/// </summary>
public class WorkflowDeploymentTests
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

    private static readonly string[] Dev = { "Developer" };
    private static readonly string[] App = { "Approver" };

    // ---- EnvironmentService ----

    [Fact]
    public async Task EnsureDefaults_CreatesDevTestProd_AndIsIdempotent()
    {
        using var db = Db();
        var svc = new EnvironmentService(new EfEnvironmentRepository(db));

        var created = await svc.EnsureDefaultsAsync();
        Assert.Equal(3, created);

        var again = await svc.EnsureDefaultsAsync();
        Assert.Equal(0, again);

        var all = await svc.ListAsync();
        Assert.Equal(new[] { "Dev", "Prod", "Test" }, all.Select(e => e.Name).ToArray());
    }

    [Fact]
    public async Task Create_RejectsDuplicateName()
    {
        using var db = Db();
        var svc = new EnvironmentService(new EfEnvironmentRepository(db));
        await svc.CreateAsync("Staging", null);

        await Assert.ThrowsAsync<BusinessException>(() => svc.CreateAsync("staging", null));
    }

    [Fact]
    public async Task Create_RejectsEmptyName()
    {
        using var db = Db();
        var svc = new EnvironmentService(new EfEnvironmentRepository(db));
        await Assert.ThrowsAsync<BusinessException>(() => svc.CreateAsync("  ", null));
    }

    // ---- WorkflowDeploymentService ----

    private static async Task<(WorkflowDeploymentService svc, Guid workflowId)> Setup(RpaDbContext db)
    {
        var envSvc = new EnvironmentService(new EfEnvironmentRepository(db));
        await envSvc.EnsureDefaultsAsync();

        var workflowId = Guid.NewGuid();
        db.WorkflowVersions.Add(new WorkflowVersion
        {
            Id = Guid.NewGuid(),
            WorkflowId = workflowId,
            Version = "1.0.0",
            Status = ComponentStatus.Draft,
        });
        await db.SaveChangesAsync();

        var svc = new WorkflowDeploymentService(
            new EfWorkflowVersionRepository(db), new EfEnvironmentRepository(db));
        return (svc, workflowId);
    }

    [Fact]
    public async Task Publish_MovesDraftToTest_TargetingTestEnvironment()
    {
        using var db = Db();
        var (svc, wf) = await Setup(db);

        var result = await svc.PublishToTestAsync(wf, "1.0.0", Dev);

        Assert.Equal(ComponentStatus.Test, result.Status);
        var testEnv = await new EfEnvironmentRepository(db).FindByNameAsync("Test");
        Assert.Equal(testEnv!.Id, result.EnvironmentId);
    }

    [Fact]
    public async Task Publish_RequiresDeveloperRole()
    {
        using var db = Db();
        var (svc, wf) = await Setup(db);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.PublishToTestAsync(wf, "1.0.0", App));
    }

    [Fact]
    public async Task Approve_RejectsDraftThatSkippedTest()
    {
        using var db = Db();
        var (svc, wf) = await Setup(db);

        await Assert.ThrowsAsync<BusinessException>(
            () => svc.ApproveToProdAsync(wf, "1.0.0", App));
    }

    [Fact]
    public async Task Approve_PromotesTestToPublished_TargetingProd()
    {
        using var db = Db();
        var (svc, wf) = await Setup(db);
        await svc.PublishToTestAsync(wf, "1.0.0", Dev);

        var result = await svc.ApproveToProdAsync(wf, "1.0.0", App);

        Assert.Equal(ComponentStatus.Published, result.Status);
        var prodEnv = await new EfEnvironmentRepository(db).FindByNameAsync("Prod");
        Assert.Equal(prodEnv!.Id, result.EnvironmentId);
    }

    [Fact]
    public async Task Approve_RequiresApproverRole()
    {
        using var db = Db();
        var (svc, wf) = await Setup(db);
        await svc.PublishToTestAsync(wf, "1.0.0", Dev);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.ApproveToProdAsync(wf, "1.0.0", Dev));
    }

    [Fact]
    public async Task Approve_IsIdempotent_WhenAlreadyPublished()
    {
        using var db = Db();
        var (svc, wf) = await Setup(db);
        await svc.PublishToTestAsync(wf, "1.0.0", Dev);
        await svc.ApproveToProdAsync(wf, "1.0.0", App);

        var result = await svc.ApproveToProdAsync(wf, "1.0.0", App);
        Assert.Equal(ComponentStatus.Published, result.Status);
    }

    [Fact]
    public async Task Publish_UnknownVersion_Throws()
    {
        using var db = Db();
        var (svc, wf) = await Setup(db);
        await Assert.ThrowsAsync<BusinessException>(
            () => svc.PublishToTestAsync(wf, "9.9.9", Dev));
    }

    [Fact]
    public async Task ListVersions_ReturnsVersionsForWorkflow()
    {
        using var db = Db();
        var (svc, wf) = await Setup(db);
        var list = await svc.ListVersionsAsync(wf);
        Assert.Single(list);
        Assert.Equal("1.0.0", list[0].Version);
    }
}

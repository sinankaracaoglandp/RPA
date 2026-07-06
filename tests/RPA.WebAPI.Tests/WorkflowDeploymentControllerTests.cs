namespace RPA.WebAPI.Tests;

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Services;
using RPA.WebAPI.Controllers;
using Environment = RPA.Domain.Entities.Environment;

/// <summary>
/// WP-6.4 — EnvironmentsController + WorkflowDeploymentController: listeleme, oluşturma,
/// publish/approve akışı, rol/durum doğrulaması. Fake repolarla (EF sağlayıcısız).
/// </summary>
public class WorkflowDeploymentControllerTests
{
    private sealed class FakeEnvRepo : IEnvironmentRepository
    {
        public readonly List<Environment> Items = new();
        public Task<IReadOnlyList<Environment>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Environment>>(Items.OrderBy(e => e.Name).ToList());
        public Task<Environment?> FindByNameAsync(string name, CancellationToken ct = default)
            => Task.FromResult(Items.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)));
        public Task<Environment> AddAsync(Environment e, CancellationToken ct = default)
        {
            Items.Add(e);
            return Task.FromResult(e);
        }
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeVersionRepo : IWorkflowVersionRepository
    {
        public readonly List<WorkflowVersion> Items = new();
        public Task<IReadOnlyList<WorkflowVersion>> ListByWorkflowAsync(Guid wf, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<WorkflowVersion>>(Items.Where(v => v.WorkflowId == wf).ToList());
        public Task<WorkflowVersion?> FindAsync(Guid wf, string version, CancellationToken ct = default)
            => Task.FromResult(Items.FirstOrDefault(v => v.WorkflowId == wf && v.Version == version));
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static void WithRoles(ControllerBase c, params string[] roles)
    {
        var identity = new ClaimsIdentity(roles.Select(r => new Claim(ClaimTypes.Role, r)), "test");
        c.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        };
    }

    // ---- EnvironmentsController ----

    [Fact]
    public async Task Environments_List_ReturnsAll()
    {
        var repo = new FakeEnvRepo();
        repo.Items.Add(new Environment { Id = Guid.NewGuid(), Name = "Prod" });
        repo.Items.Add(new Environment { Id = Guid.NewGuid(), Name = "Dev" });
        var controller = new EnvironmentsController(new EnvironmentService(repo));

        var result = await controller.List(default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<EnvironmentDto>>(ok.Value);
        Assert.Equal(new[] { "Dev", "Prod" }, list.Select(e => e.Name).ToArray());
    }

    [Fact]
    public async Task Environments_Create_Rejects_EmptyName()
    {
        var controller = new EnvironmentsController(new EnvironmentService(new FakeEnvRepo()));
        var result = await controller.Create(new CreateEnvironmentRequest { Name = "" }, default);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Environments_Create_ReturnsDto()
    {
        var controller = new EnvironmentsController(new EnvironmentService(new FakeEnvRepo()));
        var result = await controller.Create(new CreateEnvironmentRequest { Name = "Staging" }, default);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<EnvironmentDto>(ok.Value);
        Assert.Equal("Staging", dto.Name);
    }

    // ---- WorkflowDeploymentController ----

    private static (WorkflowDeploymentController controller, FakeVersionRepo versions, Guid wf) Deploy()
    {
        var envs = new FakeEnvRepo();
        envs.Items.Add(new Environment { Id = Guid.NewGuid(), Name = "Test" });
        envs.Items.Add(new Environment { Id = Guid.NewGuid(), Name = "Prod" });
        var versions = new FakeVersionRepo();
        var wf = Guid.NewGuid();
        versions.Items.Add(new WorkflowVersion
        {
            Id = Guid.NewGuid(), WorkflowId = wf, Version = "1.0.0", Status = ComponentStatus.Draft,
        });
        var controller = new WorkflowDeploymentController(new WorkflowDeploymentService(versions, envs));
        return (controller, versions, wf);
    }

    [Fact]
    public async Task Publish_MovesToTest()
    {
        var (controller, _, wf) = Deploy();
        WithRoles(controller, "Developer");

        var result = await controller.Publish(wf.ToString(), "1.0.0", default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<WorkflowVersionDto>(ok.Value);
        Assert.Equal("Test", dto.Status);
    }

    [Fact]
    public async Task Publish_BadGuid_ReturnsBadRequest()
    {
        var (controller, _, _) = Deploy();
        WithRoles(controller, "Developer");
        var result = await controller.Publish("not-a-guid", "1.0.0", default);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Approve_AfterPublish_Publishes()
    {
        var (controller, _, wf) = Deploy();
        WithRoles(controller, "Developer");
        await controller.Publish(wf.ToString(), "1.0.0", default);
        WithRoles(controller, "Approver");

        var result = await controller.Approve(wf.ToString(), "1.0.0", default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<WorkflowVersionDto>(ok.Value);
        Assert.Equal("Published", dto.Status);
    }

    [Fact]
    public async Task List_ReturnsWorkflowVersions()
    {
        var (controller, _, wf) = Deploy();
        var result = await controller.List(wf.ToString(), default);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<WorkflowVersionDto>>(ok.Value);
        Assert.Single(list);
    }
}

namespace RPA.WebAPI.Tests;

using Microsoft.AspNetCore.Mvc;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Services;
using RPA.WebAPI.Controllers;
using Environment = RPA.Domain.Entities.Environment;

/// <summary>Paket B — proje/workflow CRUD ve taslak uçları (fake repolarla).</summary>
public class ProjectsControllerTests
{
    // FakeProjectRepo / FakeWorkflowRepo / FakeEnvRepo: Task 2 test dosyasındaki
    // implementasyonların birebir kopyası (bkz. WorkflowDesignServiceTests) — buraya aynen ekle.

    private sealed class FakeProjectRepo : IProjectRepository
    {
        public readonly List<Project> Items = new();
        public readonly List<Workflow> WorkflowItems = new();
        public Task<IReadOnlyList<Project>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Project>>(Items.OrderBy(p => p.Name).ToList());
        public Task<Project?> FindAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(Items.FirstOrDefault(p => p.Id == id));
        public Task<Project> AddAsync(Project p, CancellationToken ct = default)
        { Items.Add(p); return Task.FromResult(p); }
        public Task<int> CountWorkflowsAsync(Guid projectId, CancellationToken ct = default)
            => Task.FromResult(WorkflowItems.Count(w => w.ProjectId == projectId));
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeWorkflowRepo : IWorkflowRepository
    {
        public readonly List<Workflow> Items;
        public readonly List<WorkflowVersion> Versions = new();
        public FakeWorkflowRepo(List<Workflow>? shared = null) => Items = shared ?? new List<Workflow>();
        public Task<IReadOnlyList<Workflow>> ListByProjectAsync(Guid projectId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Workflow>>(Items.Where(w => w.ProjectId == projectId).ToList());
        public Task<Workflow?> FindAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(Items.FirstOrDefault(w => w.Id == id));
        public Task<Workflow> AddAsync(Workflow w, CancellationToken ct = default)
        { Items.Add(w); return Task.FromResult(w); }
        public Task<WorkflowVersion?> FindDraftAsync(Guid workflowId, CancellationToken ct = default)
            => Task.FromResult(Versions.FirstOrDefault(
                v => v.WorkflowId == workflowId && v.Status == ComponentStatus.Draft));
        public Task AddVersionAsync(WorkflowVersion v, CancellationToken ct = default)
        { Versions.Add(v); return Task.CompletedTask; }
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeEnvRepo : IEnvironmentRepository
    {
        public readonly List<Environment> Items = new();
        public Task<IReadOnlyList<Environment>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Environment>>(Items.ToList());
        public Task<Environment?> FindByNameAsync(string name, CancellationToken ct = default)
            => Task.FromResult(Items.FirstOrDefault(
                e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)));
        public Task<Environment> AddAsync(Environment e, CancellationToken ct = default)
        { Items.Add(e); return Task.FromResult(e); }
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static WorkflowDesignService Service(
        out List<Project> projects, out List<WorkflowVersion> versions)
    {
        var projectRepo = new FakeProjectRepo();
        var workflowRepo = new FakeWorkflowRepo(projectRepo.WorkflowItems);
        projects = projectRepo.Items;
        versions = workflowRepo.Versions;
        return new WorkflowDesignService(
            projectRepo, workflowRepo, new FakeEnvRepo(),
            new RPA.Infrastructure.Workflow.WorkflowValidator());
    }

    private const string ValidJson =
        "{\"schemaVersion\":\"1.0\",\"id\":\"550e8400-e29b-41d4-a716-446655440000\",\"name\":\"Test\",\"version\":\"1.0.0\",\"nodes\":[],\"connections\":[]}";

    [Fact]
    public async Task CreateProject_ThenList_ReturnsCard()
    {
        var controller = new ProjectsController(Service(out _, out _));

        await controller.Create(new CreateProjectRequest { Name = "Pilot", Description = "d" }, default);
        var result = await controller.List(default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<ProjectDto>>(ok.Value);
        Assert.Single(list);
        Assert.Equal("Pilot", list[0].Name);
        Assert.Equal(0, list[0].WorkflowCount);
    }

    [Fact]
    public async Task CreateProject_EmptyName_Returns400()
    {
        var controller = new ProjectsController(Service(out _, out _));
        var result = await controller.Create(new CreateProjectRequest { Name = " " }, default);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task CreateWorkflow_ThenListWorkflows_ReturnsIt()
    {
        var svc = Service(out var projects, out _);
        var controller = new ProjectsController(svc);
        var created = await controller.Create(new CreateProjectRequest { Name = "Pilot" }, default);
        var projectId = ((ProjectDto)((OkObjectResult)created.Result!).Value!).Id;

        await controller.CreateWorkflow(projectId.ToString(),
            new CreateWorkflowRequest { Name = "Sipariş" }, default);
        var result = await controller.ListWorkflows(projectId.ToString(), default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<WorkflowSummaryDto>>(ok.Value);
        Assert.Single(list);
        Assert.Equal("Sipariş", list[0].Name);
    }

    [Fact]
    public async Task ListWorkflows_BadGuid_Returns400()
    {
        var controller = new ProjectsController(Service(out _, out _));
        var result = await controller.ListWorkflows("not-a-guid", default);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetDraft_ReturnsJsonDefinition()
    {
        var svc = Service(out _, out _);
        var projects = new ProjectsController(svc);
        var workflows = new WorkflowsController(svc);
        var created = await projects.Create(new CreateProjectRequest { Name = "P" }, default);
        var projectId = ((ProjectDto)((OkObjectResult)created.Result!).Value!).Id;
        var wfCreated = await projects.CreateWorkflow(projectId.ToString(),
            new CreateWorkflowRequest { Name = "wf" }, default);
        var wfId = ((WorkflowSummaryDto)((OkObjectResult)wfCreated.Result!).Value!).Id;

        var result = await workflows.GetDraft(wfId.ToString(), default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<WorkflowDraftDto>(ok.Value);
        Assert.Contains("\"nodes\"", dto.JsonDefinition);
    }

    [Fact]
    public async Task SaveDraft_ValidJson_Persists()
    {
        var svc = Service(out _, out var versions);
        var projects = new ProjectsController(svc);
        var workflows = new WorkflowsController(svc);
        var created = await projects.Create(new CreateProjectRequest { Name = "P" }, default);
        var projectId = ((ProjectDto)((OkObjectResult)created.Result!).Value!).Id;
        var wfCreated = await projects.CreateWorkflow(projectId.ToString(),
            new CreateWorkflowRequest { Name = "wf" }, default);
        var wfId = ((WorkflowSummaryDto)((OkObjectResult)wfCreated.Result!).Value!).Id;

        var result = await workflows.SaveDraft(wfId.ToString(),
            new SaveDraftRequest { JsonDefinition = ValidJson }, default);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(ValidJson, versions.Single().JsonDefinition);
    }

    [Fact]
    public async Task SaveDraft_InvalidJson_Returns400WithErrors()
    {
        var svc = Service(out _, out _);
        var projects = new ProjectsController(svc);
        var workflows = new WorkflowsController(svc);
        var created = await projects.Create(new CreateProjectRequest { Name = "P" }, default);
        var projectId = ((ProjectDto)((OkObjectResult)created.Result!).Value!).Id;
        var wfCreated = await projects.CreateWorkflow(projectId.ToString(),
            new CreateWorkflowRequest { Name = "wf" }, default);
        var wfId = ((WorkflowSummaryDto)((OkObjectResult)wfCreated.Result!).Value!).Id;

        var result = await workflows.SaveDraft(wfId.ToString(),
            new SaveDraftRequest { JsonDefinition = "{\"nodes\":\"bozuk\"}" }, default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }
}

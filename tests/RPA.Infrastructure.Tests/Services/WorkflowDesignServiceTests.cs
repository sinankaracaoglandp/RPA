namespace RPA.Infrastructure.Tests.Services;

using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Services;
using System.Text.Json;
using BusinessException = RPA.Domain.Exceptions.BusinessException;
using Environment = RPA.Domain.Entities.Environment;
using Xunit;

public class WorkflowDesignServiceTests
{
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

    private static (WorkflowDesignService svc, FakeProjectRepo projects, FakeWorkflowRepo workflows, FakeEnvRepo envs) Make()
    {
        var projects = new FakeProjectRepo();
        var workflows = new FakeWorkflowRepo(projects.WorkflowItems);
        var envs = new FakeEnvRepo();
        return (new WorkflowDesignService(projects, workflows, envs, new RPA.Infrastructure.Workflow.WorkflowValidator()), projects, workflows, envs);
    }

    private const string ValidJson =
        "{\"schemaVersion\":\"1.0\",\"id\":\"550e8400-e29b-41d4-a716-446655440000\",\"name\":\"Test\",\"version\":\"1.0.0\",\"nodes\":[],\"connections\":[]}";

    [Fact]
    public async Task CreateProject_EmptyName_Throws()
    {
        var (svc, _, _, _) = Make();
        await Assert.ThrowsAsync<BusinessException>(() => svc.CreateProjectAsync("", null));
    }

    [Fact]
    public async Task ListProjects_ReturnsWorkflowCounts()
    {
        var (svc, _, _, _) = Make();
        var p = await svc.CreateProjectAsync("Pilot", "açıklama");
        await svc.CreateWorkflowAsync(p.Id, "wf-a");
        await svc.CreateWorkflowAsync(p.Id, "wf-b");

        var list = await svc.ListProjectsAsync();

        Assert.Single(list);
        Assert.Equal(2, list[0].WorkflowCount);
    }

    [Fact]
    public async Task CreateWorkflow_UnknownProject_Throws()
    {
        var (svc, _, _, _) = Make();
        await Assert.ThrowsAsync<BusinessException>(() => svc.CreateWorkflowAsync(Guid.NewGuid(), "wf"));
    }

    [Fact]
    public async Task CreateWorkflow_CreatesEmptyDraftVersion()
    {
        var (svc, _, workflows, _) = Make();
        var p = await svc.CreateProjectAsync("Pilot", null);

        var wf = await svc.CreateWorkflowAsync(p.Id, "Sipariş Aktarımı");

        var draft = await workflows.FindDraftAsync(wf.Id);
        Assert.NotNull(draft);
        Assert.Equal(ComponentStatus.Draft, draft!.Status);
        Assert.Contains("\"nodes\"", draft.JsonDefinition);
    }

    [Fact]
    public async Task GetDraft_MissingDraft_CreatesOne()
    {
        var (svc, projects, workflows, _) = Make();
        var p = await svc.CreateProjectAsync("Pilot", null);
        var wf = await svc.CreateWorkflowAsync(p.Id, "wf");
        workflows.Versions.Clear(); // taslağı yapay olarak kaldır

        var draft = await svc.GetDraftAsync(wf.Id);

        Assert.Equal(ComponentStatus.Draft, draft.Status);
        Assert.Single(workflows.Versions);
    }

    [Fact]
    public async Task SaveDraft_ValidJson_UpdatesExistingDraft_NoNewVersion()
    {
        var (svc, _, workflows, _) = Make();
        var p = await svc.CreateProjectAsync("Pilot", null);
        var wf = await svc.CreateWorkflowAsync(p.Id, "wf");

        var saved = await svc.SaveDraftAsync(wf.Id, ValidJson);

        Assert.Equal(ValidJson, saved.JsonDefinition);
        Assert.Single(workflows.Versions); // güncelleme, yeni versiyon değil
    }

    [Fact]
    public async Task SaveDraft_InvalidJson_ThrowsBusinessException()
    {
        var (svc, _, _, _) = Make();
        var p = await svc.CreateProjectAsync("Pilot", null);
        var wf = await svc.CreateWorkflowAsync(p.Id, "wf");

        await Assert.ThrowsAsync<BusinessException>(
            () => svc.SaveDraftAsync(wf.Id, "{\"nodes\":\"bozuk\"}"));
    }

    [Fact]
    public async Task Draft_UsesDevEnvironment_AutoCreatesWhenMissing()
    {
        var (svc, _, workflows, envs) = Make();
        var p = await svc.CreateProjectAsync("Pilot", null);
        var wf = await svc.CreateWorkflowAsync(p.Id, "wf");

        Assert.Contains(envs.Items, e => e.Name == "Dev");
        var draft = await workflows.FindDraftAsync(wf.Id);
        Assert.Equal(envs.Items.First(e => e.Name == "Dev").Id, draft!.EnvironmentId);
    }

    [Fact]
    public async Task CreateWorkflow_NameWithBackslashAndNewline_DraftJsonIsValidAndRoundTrips()
    {
        var (svc, _, workflows, _) = Make();
        var p = await svc.CreateProjectAsync("Pilot", null);
        var testName = "Ad\\ı\n\"test\""; // backslash, newline, quotes

        var wf = await svc.CreateWorkflowAsync(p.Id, testName);

        var draft = await workflows.FindDraftAsync(wf.Id);
        Assert.NotNull(draft);

        // Parse JSON to verify it's valid
        var doc = JsonDocument.Parse(draft!.JsonDefinition);
        var root = doc.RootElement;

        // Extract the name property and verify it round-trips
        var nameProperty = root.GetProperty("name");
        var roundTrippedName = nameProperty.GetString();

        Assert.Equal(testName, roundTrippedName);
    }
}

namespace RPA.Infrastructure.Services;

using System.Text.Json;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Workflow;
using BusinessException = RPA.Domain.Exceptions.BusinessException;
using Environment = RPA.Domain.Entities.Environment;

/// <summary>
/// Studio tasarım-zamanı kalıcılık akışı (Paket B): proje/workflow oluşturma-listeleme
/// ve taslak (Status == Draft) kaydet/yükle. Taslak tek kayıttır; kaydetme mevcut
/// taslağın JsonDefinition'ını günceller, yeni versiyon YARATMAZ (yayınlama
/// WorkflowDeploymentService'te kalır). JSON, kontrat şeması v1.0'a karşı doğrulanır.
/// </summary>
public sealed class WorkflowDesignService
{
    /// <summary>Taslakların bağlandığı ortam; yoksa otomatik oluşturulur.</summary>
    public const string DraftEnvironmentName = "Dev";

    private readonly IProjectRepository _projects;
    private readonly IWorkflowRepository _workflows;
    private readonly IEnvironmentRepository _environments;
    private readonly WorkflowValidator _validator;

    public WorkflowDesignService(
        IProjectRepository projects,
        IWorkflowRepository workflows,
        IEnvironmentRepository environments,
        WorkflowValidator validator)
    {
        _projects = projects ?? throw new ArgumentNullException(nameof(projects));
        _workflows = workflows ?? throw new ArgumentNullException(nameof(workflows));
        _environments = environments ?? throw new ArgumentNullException(nameof(environments));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public async Task<IReadOnlyList<(Project Project, int WorkflowCount)>> ListProjectsAsync(
        CancellationToken ct = default)
    {
        var projects = await _projects.ListAsync(ct).ConfigureAwait(false);
        var result = new List<(Project, int)>(projects.Count);
        foreach (var p in projects)
        {
            result.Add((p, await _projects.CountWorkflowsAsync(p.Id, ct).ConfigureAwait(false)));
        }
        return result;
    }

    public async Task<Project> CreateProjectAsync(
        string name, string? description, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new BusinessException("Proje adı boş olamaz.");
        }
        var project = new Project { Id = Guid.NewGuid(), Name = name.Trim(), Description = description };
        await _projects.AddAsync(project, ct).ConfigureAwait(false);
        await _projects.SaveChangesAsync(ct).ConfigureAwait(false);
        return project;
    }

    public async Task<IReadOnlyList<Workflow>> ListWorkflowsAsync(
        Guid projectId, CancellationToken ct = default)
    {
        _ = await RequireProject(projectId, ct).ConfigureAwait(false);
        return await _workflows.ListByProjectAsync(projectId, ct).ConfigureAwait(false);
    }

    public async Task<Workflow> CreateWorkflowAsync(
        Guid projectId, string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new BusinessException("Workflow adı boş olamaz.");
        }
        _ = await RequireProject(projectId, ct).ConfigureAwait(false);

        var workflow = new Workflow { Id = Guid.NewGuid(), ProjectId = projectId, Name = name.Trim() };
        await _workflows.AddAsync(workflow, ct).ConfigureAwait(false);
        await CreateDraft(workflow, ct).ConfigureAwait(false);
        await _workflows.SaveChangesAsync(ct).ConfigureAwait(false);
        return workflow;
    }

    public async Task<WorkflowVersion> GetDraftAsync(Guid workflowId, CancellationToken ct = default)
    {
        var workflow = await RequireWorkflow(workflowId, ct).ConfigureAwait(false);
        var draft = await _workflows.FindDraftAsync(workflowId, ct).ConfigureAwait(false);
        if (draft is null)
        {
            draft = await CreateDraft(workflow, ct).ConfigureAwait(false);
            await _workflows.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        return draft;
    }

    public async Task<WorkflowVersion> SaveDraftAsync(
        Guid workflowId, string jsonDefinition, CancellationToken ct = default)
    {
        var validation = _validator.ValidateWorkflowJson(jsonDefinition);
        if (!validation.IsValid)
        {
            throw new BusinessException(
                $"Workflow JSON şema doğrulaması başarısız: {string.Join("; ", validation.Errors)}");
        }

        var workflow = await RequireWorkflow(workflowId, ct).ConfigureAwait(false);
        var draft = await _workflows.FindDraftAsync(workflowId, ct).ConfigureAwait(false)
            ?? await CreateDraft(workflow, ct).ConfigureAwait(false);

        draft.JsonDefinition = jsonDefinition;
        await _workflows.SaveChangesAsync(ct).ConfigureAwait(false);
        return draft;
    }

    private async Task<WorkflowVersion> CreateDraft(Workflow workflow, CancellationToken ct)
    {
        var env = await _environments.FindByNameAsync(DraftEnvironmentName, ct).ConfigureAwait(false);
        if (env is null)
        {
            env = new Environment { Id = Guid.NewGuid(), Name = DraftEnvironmentName };
            await _environments.AddAsync(env, ct).ConfigureAwait(false);
        }

        var draft = new WorkflowVersion
        {
            Id = Guid.NewGuid(),
            WorkflowId = workflow.Id,
            Version = "1.0.0",
            Status = ComponentStatus.Draft,
            EnvironmentId = env.Id,
            JsonDefinition = EmptyDefinition(workflow),
        };
        await _workflows.AddVersionAsync(draft, ct).ConfigureAwait(false);
        return draft;
    }

    private static string EmptyDefinition(Workflow workflow)
    {
        var definition = new
        {
            schemaVersion = "1.0",
            id = workflow.Id,
            name = workflow.Name,
            version = "1.0.0",
            nodes = Array.Empty<object>(),
            connections = Array.Empty<object>(),
            variables = Array.Empty<object>()
        };
        return JsonSerializer.Serialize(definition);
    }

    private async Task<Project> RequireProject(Guid id, CancellationToken ct)
        => await _projects.FindAsync(id, ct).ConfigureAwait(false)
            ?? throw new BusinessException($"Proje bulunamadı: {id}");

    private async Task<Workflow> RequireWorkflow(Guid id, CancellationToken ct)
        => await _workflows.FindAsync(id, ct).ConfigureAwait(false)
            ?? throw new BusinessException($"Workflow bulunamadı: {id}");
}

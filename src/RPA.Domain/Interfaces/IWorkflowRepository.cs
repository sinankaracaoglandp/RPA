namespace RPA.Domain.Interfaces;

using RPA.Domain.Entities;

/// <summary>Workflow + taslak versiyon kalıcılık soyutlaması (Paket B).</summary>
public interface IWorkflowRepository
{
    Task<IReadOnlyList<Workflow>> ListByProjectAsync(Guid projectId, CancellationToken ct = default);
    Task<Workflow?> FindAsync(Guid id, CancellationToken ct = default);
    Task<Workflow> AddAsync(Workflow workflow, CancellationToken ct = default);
    /// <summary>Workflow'un Status == Draft olan tek taslak versiyonu; yoksa null.</summary>
    Task<WorkflowVersion?> FindDraftAsync(Guid workflowId, CancellationToken ct = default);
    Task AddVersionAsync(WorkflowVersion version, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

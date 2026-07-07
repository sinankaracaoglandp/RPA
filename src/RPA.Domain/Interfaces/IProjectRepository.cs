namespace RPA.Domain.Interfaces;

using RPA.Domain.Entities;

/// <summary>Proje kalıcılık soyutlaması (Paket B — Studio Projelerim).</summary>
public interface IProjectRepository
{
    Task<IReadOnlyList<Project>> ListAsync(CancellationToken ct = default);
    Task<Project?> FindAsync(Guid id, CancellationToken ct = default);
    Task<Project> AddAsync(Project project, CancellationToken ct = default);
    /// <summary>Projedeki (soft-delete hariç) workflow sayısı — liste kartı için.</summary>
    Task<int> CountWorkflowsAsync(Guid projectId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

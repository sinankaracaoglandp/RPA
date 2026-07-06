namespace RPA.Domain.Interfaces;

using RPA.Domain.Entities;

/// <summary>
/// Workflow versiyon kalıcılık soyutlaması (WP-6.4). Deployment governance akışının
/// (Draft → Test → Published) ve ortam hedeflemesinin gerektirdiği okuma/güncelleme.
/// </summary>
public interface IWorkflowVersionRepository
{
    /// <summary>Bir workflow'un tüm versiyonları (en yeni önce).</summary>
    Task<IReadOnlyList<WorkflowVersion>> ListByWorkflowAsync(Guid workflowId, CancellationToken cancellationToken = default);

    /// <summary>workflowId + version ile tekil kayıt; yoksa null.</summary>
    Task<WorkflowVersion?> FindAsync(Guid workflowId, string version, CancellationToken cancellationToken = default);

    /// <summary>Değişiklikleri kalıcı hale getirir.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

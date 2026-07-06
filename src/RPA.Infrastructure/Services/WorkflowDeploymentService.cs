namespace RPA.Infrastructure.Services;

using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;
using BusinessException = RPA.Domain.Exceptions.BusinessException;
using Environment = RPA.Domain.Entities.Environment;

/// <summary>
/// Workflow deployment governance akışı (WP-6.4, Spec Bölüm 9 — publish/approve).
/// Ortamlar arası yaşam döngüsü:
/// <list type="bullet">
///   <item>Draft → Test: geliştirici Test ortamına yayınlar ("Developer" rolü).</item>
///   <item>Test → Published: onaylayan Prod ortamına terfi ettirir ("Approver" rolü).</item>
/// </list>
/// Prod'da yalnızca Published versiyon koşabilir; Draft doğrudan Prod'a terfi edemez
/// (önce Test'ten geçmelidir).
/// </summary>
public sealed class WorkflowDeploymentService
{
    public const string DeveloperRole = "Developer";
    public const string ApproverRole = "Approver";

    private readonly IWorkflowVersionRepository _versions;
    private readonly IEnvironmentRepository _environments;

    public WorkflowDeploymentService(
        IWorkflowVersionRepository versions,
        IEnvironmentRepository environments)
    {
        _versions = versions ?? throw new ArgumentNullException(nameof(versions));
        _environments = environments ?? throw new ArgumentNullException(nameof(environments));
    }

    /// <summary>Bir workflow'un tüm versiyonlarını (deployment durumu ile) listeler.</summary>
    public Task<IReadOnlyList<WorkflowVersion>> ListVersionsAsync(Guid workflowId, CancellationToken ct = default)
        => _versions.ListByWorkflowAsync(workflowId, ct);

    /// <summary>
    /// Draft durumundaki bir versiyonu Test ortamına yayınlar (Status → Test).
    /// Yalnızca "Developer" rolü çağırabilir.
    /// </summary>
    public async Task<WorkflowVersion> PublishToTestAsync(
        Guid workflowId, string version, IEnumerable<string> callerRoles, CancellationToken ct = default)
    {
        RequireRole(callerRoles, DeveloperRole, "Test'e yayınlama");

        var entity = await Require(workflowId, version, ct).ConfigureAwait(false);

        if (entity.Status is ComponentStatus.Published)
        {
            throw new BusinessException(
                $"Versiyon {version} zaten Published; yeni bir versiyon oluşturun.");
        }

        var testEnv = await RequireEnvironment("Test", ct).ConfigureAwait(false);
        entity.Status = ComponentStatus.Test;
        entity.EnvironmentId = testEnv.Id;

        await _versions.SaveChangesAsync(ct).ConfigureAwait(false);
        return entity;
    }

    /// <summary>
    /// Test durumundaki bir versiyonu onaylar ve Prod ortamına terfi ettirir (Status → Published).
    /// Yalnızca "Approver" rolü çağırabilir. Test'ten geçmemiş (Draft) versiyon onaylanamaz.
    /// </summary>
    public async Task<WorkflowVersion> ApproveToProdAsync(
        Guid workflowId, string version, IEnumerable<string> callerRoles, CancellationToken ct = default)
    {
        RequireRole(callerRoles, ApproverRole, "Prod'a onaylama");

        var entity = await Require(workflowId, version, ct).ConfigureAwait(false);

        if (entity.Status is ComponentStatus.Published)
        {
            return entity; // idempotent
        }

        if (entity.Status is not ComponentStatus.Test)
        {
            throw new BusinessException(
                $"Versiyon {version} önce Test ortamına yayınlanmalıdır (mevcut durum: {entity.Status}).");
        }

        var prodEnv = await RequireEnvironment("Prod", ct).ConfigureAwait(false);
        entity.Status = ComponentStatus.Published;
        entity.EnvironmentId = prodEnv.Id;

        await _versions.SaveChangesAsync(ct).ConfigureAwait(false);
        return entity;
    }

    private async Task<WorkflowVersion> Require(Guid workflowId, string version, CancellationToken ct)
        => await _versions.FindAsync(workflowId, version, ct).ConfigureAwait(false)
            ?? throw new BusinessException($"Workflow {workflowId} versiyon {version} bulunamadı.");

    private async Task<Environment> RequireEnvironment(string name, CancellationToken ct)
        => await _environments.FindByNameAsync(name, ct).ConfigureAwait(false)
            ?? throw new BusinessException($"'{name}' ortamı tanımlı değil; önce ortamları oluşturun.");

    private static void RequireRole(IEnumerable<string> callerRoles, string requiredRole, string action)
    {
        var roles = callerRoles ?? Array.Empty<string>();
        if (!roles.Any(r => string.Equals(r, requiredRole, StringComparison.OrdinalIgnoreCase)))
        {
            throw new UnauthorizedAccessException(
                $"Bu işlem ({action}) yalnızca '{requiredRole}' rolüne sahip kullanıcılar tarafından yapılabilir.");
        }
    }
}

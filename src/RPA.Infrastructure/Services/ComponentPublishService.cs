namespace RPA.Infrastructure.Services;

using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;
using BusinessException = RPA.Domain.Exceptions.BusinessException;

/// <summary>
/// Component versiyonlama + publish/approve governance akışı (Spec Bölüm 9, WP-4.5 — ilk
/// uçtan uca publish/approve doğrulaması). Akış: Draft (publish isteği, "Developer" rolü) →
/// Published (onay, "Approver" rolü). Component'ler Published olmadan Prod'da koşamaz
/// (versiyon pinlenmemiş komponent çağrıları yalnızca Published versiyonu görür —
/// bkz. <see cref="RPA.Infrastructure.Workflow.ComponentResolver"/>).
/// </summary>
public sealed class ComponentPublishService
{
    public const string DeveloperRole = "Developer";
    public const string ApproverRole = "Approver";

    private readonly IComponentPublishRepository _repository;

    public ComponentPublishService(IComponentPublishRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <summary>
    /// Bir component versiyonunu yayına hazırlar: Draft durumunda kaydedilir, onay bekler.
    /// Yalnızca "Developer" rolündeki kullanıcılar çağırabilir.
    /// </summary>
    public async Task<ComponentVersion> PublishAsync(
        Guid componentId,
        string version,
        string jsonDefinition,
        string inputOutputSchema,
        IEnumerable<string> callerRoles,
        CancellationToken ct = default)
    {
        RequireRole(callerRoles, DeveloperRole, "yayına hazırlama");

        if (string.IsNullOrWhiteSpace(version))
        {
            throw new BusinessException("'version' parametresi boş olamaz (SemVer, örn. 1.0.0).");
        }
        if (string.IsNullOrWhiteSpace(jsonDefinition))
        {
            throw new BusinessException("'jsonDefinition' boş olamaz.");
        }

        var entity = new ComponentVersion
        {
            Id = Guid.NewGuid(),
            ComponentId = componentId,
            Version = version,
            JsonDefinition = jsonDefinition,
            InputOutputSchema = inputOutputSchema ?? "{}",
            Status = ComponentStatus.Draft,
        };

        return await _repository.AddAsync(entity, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Draft/Test durumundaki bir versiyonu onaylar ve Published'a taşır.
    /// Yalnızca "Approver" rolündeki kullanıcılar çağırabilir (Spec Bölüm 9 — component'lerde
    /// onay zorunlu, birden çok projeyi etkiler).
    /// </summary>
    public async Task<ComponentVersion> ApproveAsync(
        Guid componentId,
        string version,
        IEnumerable<string> callerRoles,
        CancellationToken ct = default)
    {
        RequireRole(callerRoles, ApproverRole, "onaylama");

        var existing = await _repository.FindAsync(componentId, version, ct).ConfigureAwait(false)
            ?? throw new BusinessException($"Component {componentId} versiyon {version} bulunamadı.");

        if (existing.Status == ComponentStatus.Published)
        {
            return existing;
        }

        return await _repository.UpdateStatusAsync(componentId, version, ComponentStatus.Published, ct)
            .ConfigureAwait(false);
    }

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

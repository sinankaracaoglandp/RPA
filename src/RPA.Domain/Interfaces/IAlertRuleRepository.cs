namespace RPA.Domain.Interfaces;

using RPA.Domain.Entities;

/// <summary>AlertRule kalıcılık soyutlaması (WP-6.3).</summary>
public interface IAlertRuleRepository
{
    /// <summary>Aktif (IsActive) kurallar — motorun değerlendirdiği set.</summary>
    Task<IReadOnlyList<AlertRule>> ListActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Tüm kurallar (yönetim ekranı).</summary>
    Task<IReadOnlyList<AlertRule>> ListAllAsync(CancellationToken cancellationToken = default);

    Task<AlertRule?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddAsync(AlertRule rule, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

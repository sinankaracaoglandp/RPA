namespace RPA.Domain.Interfaces;

using RPA.Domain.Entities;
using RPA.Domain.Enums;

/// <summary>
/// Component publish/approve iş akışının kalıcılık soyutlaması (Spec Bölüm 9, WP-4.5).
/// <see cref="IComponentStore"/>'un salt-okunur sözleşmesine ek olarak yazma
/// operasyonlarını (Draft ekleme, durum değiştirme) tanımlar. Böylece mevcut
/// <see cref="IComponentStore"/> kontratı değişmeden component publish/approve
/// akışı ayrı bir arayüzle eklenebilir (additive — kontrat değişikliği gerektirmez).
/// </summary>
public interface IComponentPublishRepository
{
    /// <summary>Yeni bir Draft <see cref="ComponentVersion"/> ekler.</summary>
    Task<ComponentVersion> AddAsync(ComponentVersion version, CancellationToken ct = default);

    /// <summary>Belirli bir component+versiyon kaydını bulur.</summary>
    Task<ComponentVersion?> FindAsync(Guid componentId, string version, CancellationToken ct = default);

    /// <summary>Bir versiyonun durumunu günceller (örn. Draft → Published).</summary>
    Task<ComponentVersion> UpdateStatusAsync(Guid componentId, string version, ComponentStatus status, CancellationToken ct = default);
}

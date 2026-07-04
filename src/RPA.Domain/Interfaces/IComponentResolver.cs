namespace RPA.Domain.Interfaces;

using RPA.Domain.Entities;

/// <summary>
/// Bir <c>componentCall</c> referansını (componentId + opsiyonel versiyon) çalıştırılabilir
/// <see cref="ComponentVersion"/>'a çözer. Versiyon pinleme (Spec Bölüm 5.4):
/// versiyon verilirse birebir o versiyon; verilmezse en yüksek SemVer'e sahip
/// <see cref="Enums.ComponentStatus.Published"/> versiyon seçilir.
/// </summary>
public interface IComponentResolver
{
    /// <summary>
    /// Component'i çözer.
    /// </summary>
    /// <param name="componentId">Component tanımlayıcısı (Guid string veya ad).</param>
    /// <param name="version">Pinlenmiş SemVer; null/boş ise en son Published.</param>
    /// <returns>Bulunan <see cref="ComponentVersion"/>; bulunamazsa <c>null</c>.</returns>
    ComponentVersion? Resolve(string componentId, string? version);
}

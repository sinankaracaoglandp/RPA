namespace RPA.Domain.Interfaces;

using RPA.Domain.Entities;

/// <summary>
/// Bir workflow çalıştırması sırasında çağrılan component versiyonlarını izler.
/// Toplanan <see cref="ComponentUsage"/> kayıtları bağımlılık takibi / etki analizi
/// için kalıcılaştırılabilir (Spec Bölüm 5.4 — component sürüm bağımlılığı).
/// </summary>
public interface IComponentUsageTracker
{
    /// <summary>
    /// Bir workflow versiyonunun bir component versiyonunu kullandığını kaydeder.
    /// Aynı (workflowVersion, componentVersion) çifti tekilleştirilir.
    /// </summary>
    void Record(Guid workflowVersionId, ComponentVersion componentVersion);

    /// <summary>Bu izleyicinin şimdiye dek topladığı benzersiz kullanım kayıtları.</summary>
    IReadOnlyCollection<ComponentUsage> Usages { get; }
}

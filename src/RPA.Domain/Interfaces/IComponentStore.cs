namespace RPA.Domain.Interfaces;

using RPA.Domain.Entities;

/// <summary>
/// Component versiyonlarının kaynağı (repository soyutlaması). <see cref="IComponentResolver"/>
/// versiyon pinleme kararını bu depodan dönen versiyonlar üzerinden verir.
/// EF Core ile veya bellek-içi test deposu ile implemente edilebilir.
/// </summary>
public interface IComponentStore
{
    /// <summary>Belirtilen component için bilinen tüm versiyonları döndürür (sırasız olabilir).</summary>
    IReadOnlyList<ComponentVersion> GetVersions(string componentId);
}

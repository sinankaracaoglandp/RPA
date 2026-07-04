namespace RPA.Infrastructure.Workflow;

using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;

/// <summary>
/// <see cref="IComponentResolver"/> — versiyon pinleme mantığı (Spec Bölüm 5.4).
/// <list type="bullet">
/// <item>Versiyon verilirse: birebir o SemVer'e sahip versiyon (durum ne olursa olsun,
/// pinleme bilinçli bir seçimdir) döner.</item>
/// <item>Versiyon boş ise: yalnızca <see cref="ComponentStatus.Published"/> versiyonlar
/// arasından en yüksek SemVer seçilir.</item>
/// </list>
/// Versiyon kaynağı <see cref="IComponentStore"/> soyutlamasıdır.
/// </summary>
public sealed class ComponentResolver : IComponentResolver
{
    private readonly IComponentStore _store;

    public ComponentResolver(IComponentStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc />
    public ComponentVersion? Resolve(string componentId, string? version)
    {
        if (string.IsNullOrWhiteSpace(componentId))
        {
            return null;
        }

        var versions = _store.GetVersions(componentId);
        if (versions is null || versions.Count == 0)
        {
            return null;
        }

        // Pinlenmiş versiyon: birebir string eşleşmesi (varsa), aksi halde SemVer eşitliği.
        if (!string.IsNullOrWhiteSpace(version))
        {
            var exact = versions.FirstOrDefault(v =>
                string.Equals(v.Version, version, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
            {
                return exact;
            }

            if (SemanticVersion.TryParse(version, out var pinned))
            {
                return versions.FirstOrDefault(v =>
                    SemanticVersion.TryParse(v.Version, out var sv) && sv.CompareTo(pinned) == 0);
            }

            return null;
        }

        // Versiyon yok: en yüksek SemVer'e sahip Published versiyon.
        ComponentVersion? best = null;
        var bestSemver = default(SemanticVersion);
        foreach (var v in versions)
        {
            if (v.Status != ComponentStatus.Published)
            {
                continue;
            }
            if (!SemanticVersion.TryParse(v.Version, out var sv))
            {
                continue;
            }
            if (best is null || sv.CompareTo(bestSemver) > 0)
            {
                best = v;
                bestSemver = sv;
            }
        }
        return best;
    }
}

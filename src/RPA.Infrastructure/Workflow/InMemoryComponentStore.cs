namespace RPA.Infrastructure.Workflow;

using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;

/// <summary>
/// Bellek-içi <see cref="IComponentStore"/> — testler ve DB henüz devreye alınmadan
/// önceki yapılandırma için. Component'ler <see cref="ComponentVersion.ComponentId"/>
/// Guid'ine göre indekslenir; string anahtar Guid'e ayrıştırılamazsa ad üzerinden aranmaz.
/// </summary>
public sealed class InMemoryComponentStore : IComponentStore
{
    private readonly Dictionary<string, List<ComponentVersion>> _byKey =
        new(StringComparer.OrdinalIgnoreCase);

    public InMemoryComponentStore(IEnumerable<ComponentVersion>? versions = null)
    {
        if (versions is not null)
        {
            foreach (var v in versions)
            {
                Add(v);
            }
        }
    }

    /// <summary>Bir versiyonu depoya ekler. Anahtar: ComponentId Guid string'i.</summary>
    public InMemoryComponentStore Add(ComponentVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        var key = version.ComponentId.ToString();
        if (!_byKey.TryGetValue(key, out var list))
        {
            list = new List<ComponentVersion>();
            _byKey[key] = list;
        }
        list.Add(version);
        return this;
    }

    /// <inheritdoc />
    public IReadOnlyList<ComponentVersion> GetVersions(string componentId)
    {
        if (!string.IsNullOrWhiteSpace(componentId) &&
            _byKey.TryGetValue(componentId, out var list))
        {
            return list;
        }
        return Array.Empty<ComponentVersion>();
    }

    /// <inheritdoc />
    public Task<List<ComponentVersion>> GetPublishedVersionsAsync()
    {
        var published = _byKey.Values
            .SelectMany(v => v)
            .Where(v => v.Status == ComponentStatus.Published)
            .ToList();
        return Task.FromResult(published);
    }
}

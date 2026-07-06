namespace RPA.Infrastructure.Workflow;

using System.Collections.Concurrent;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;

/// <summary>
/// Bellek-içi <see cref="IComponentPublishRepository"/> — DB entegrasyonu henüz devreye
/// alınmadan önce publish/approve akışının uçtan uca test edilmesi için (Task 4.5).
/// Aynı instance <see cref="IComponentStore"/> olarak da kullanılabilir; böylece
/// <see cref="ComponentResolver"/> yayınlanmış versiyonları görebilir.
/// </summary>
public sealed class InMemoryComponentPublishRepository : IComponentPublishRepository, IComponentStore
{
    private readonly ConcurrentDictionary<string, List<ComponentVersion>> _byComponent =
        new(StringComparer.OrdinalIgnoreCase);

    public Task<ComponentVersion> AddAsync(ComponentVersion version, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(version);
        var key = version.ComponentId.ToString();
        var list = _byComponent.GetOrAdd(key, _ => new List<ComponentVersion>());
        lock (list)
        {
            if (list.Any(v => string.Equals(v.Version, version.Version, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"Component {version.ComponentId} için versiyon {version.Version} zaten mevcut.");
            }
            list.Add(version);
        }
        return Task.FromResult(version);
    }

    public Task<ComponentVersion?> FindAsync(Guid componentId, string version, CancellationToken ct = default)
    {
        var key = componentId.ToString();
        if (_byComponent.TryGetValue(key, out var list))
        {
            lock (list)
            {
                var found = list.FirstOrDefault(v =>
                    string.Equals(v.Version, version, StringComparison.OrdinalIgnoreCase));
                return Task.FromResult(found);
            }
        }
        return Task.FromResult<ComponentVersion?>(null);
    }

    public Task<ComponentVersion> UpdateStatusAsync(
        Guid componentId, string version, ComponentStatus status, CancellationToken ct = default)
    {
        var key = componentId.ToString();
        if (_byComponent.TryGetValue(key, out var list))
        {
            lock (list)
            {
                var found = list.FirstOrDefault(v =>
                    string.Equals(v.Version, version, StringComparison.OrdinalIgnoreCase));
                if (found is not null)
                {
                    found.Status = status;
                    return Task.FromResult(found);
                }
            }
        }
        throw new KeyNotFoundException($"Component {componentId} versiyon {version} bulunamadı.");
    }

    /// <inheritdoc />
    public IReadOnlyList<ComponentVersion> GetVersions(string componentId)
    {
        if (!string.IsNullOrWhiteSpace(componentId) && _byComponent.TryGetValue(componentId, out var list))
        {
            lock (list)
            {
                return list.ToList();
            }
        }
        return Array.Empty<ComponentVersion>();
    }

    /// <inheritdoc />
    public Task<List<ComponentVersion>> GetPublishedVersionsAsync()
    {
        var published = new List<ComponentVersion>();
        foreach (var list in _byComponent.Values)
        {
            lock (list)
            {
                published.AddRange(list.Where(v => v.Status == ComponentStatus.Published));
            }
        }
        return Task.FromResult(published);
    }
}

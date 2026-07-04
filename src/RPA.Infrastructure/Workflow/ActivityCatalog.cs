namespace RPA.Infrastructure.Workflow;

using RPA.Domain.Interfaces;

/// <summary>
/// Aktivite kataloğu — Studio Toolbox'ının ve BaseRunner'ın okuduğu tek referans.
/// Singleton olarak DI'a kaydedilir. Metadata <see cref="ActivityRegistry"/>'den yüklenir.
/// Spec Bölüm 5.3.
/// </summary>
public sealed class ActivityCatalog
{
    private readonly IReadOnlyDictionary<string, ActivityMetadata> _activities;

    /// <summary>Varsayılan katalog (tüm MVP aktiviteleri).</summary>
    public ActivityCatalog()
        : this(ActivityRegistry.BuildCatalog())
    {
    }

    /// <summary>Test/özelleştirme için özel katalog verir.</summary>
    public ActivityCatalog(IReadOnlyDictionary<string, ActivityMetadata> activities)
    {
        _activities = activities ?? throw new ArgumentNullException(nameof(activities));
    }

    /// <summary>Verilen ID için metadata döndürür; yoksa null.</summary>
    public ActivityMetadata? GetActivityMetadata(string activityId)
    {
        if (string.IsNullOrWhiteSpace(activityId))
        {
            return null;
        }
        return _activities.TryGetValue(activityId, out var meta) ? meta : null;
    }

    /// <summary>Belirtilen capability/etiketi gerektiren aktiviteleri listeler.</summary>
    public IReadOnlyList<ActivityMetadata> ListActivitiesByCapability(string capability)
    {
        if (string.IsNullOrWhiteSpace(capability))
        {
            return Array.Empty<ActivityMetadata>();
        }
        return _activities.Values
            .Where(a => a.RequiredCapabilities.Contains(capability, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>Bir kategoriye ait aktiviteleri listeler (Toolbox gruplaması).</summary>
    public IReadOnlyList<ActivityMetadata> ListActivitiesByCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return Array.Empty<ActivityMetadata>();
        }
        return _activities.Values
            .Where(a => string.Equals(a.Category, category, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>Tüm katalog (Studio Toolbox popülasyonu için).</summary>
    public IReadOnlyDictionary<string, ActivityMetadata> ListAll() => _activities;

    /// <summary>Kayıtlı aktivite sayısı.</summary>
    public int Count => _activities.Count;
}

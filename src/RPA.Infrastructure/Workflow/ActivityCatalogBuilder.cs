namespace RPA.Infrastructure.Workflow;

using RPA.Domain.Enums;
using RPA.Domain.Interfaces;

/// <summary>
/// Aktivite metadata'sını okunabilir bir fluent API ile kaydetmeyi sağlar.
/// <see cref="ActivityRegistry"/> tüm katalog girişlerini bununla tanımlar.
/// </summary>
public sealed class ActivityCatalogBuilder
{
    private readonly Dictionary<string, ActivityMetadata> _activities = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Yeni bir aktivite tanımı başlatır (dot-notation ID, örn. "Sap.Nco.CallBapi").</summary>
    public ActivityBuilder Activity(string activityId)
    {
        if (string.IsNullOrWhiteSpace(activityId))
        {
            throw new ArgumentException("Activity ID boş olamaz.", nameof(activityId));
        }

        if (_activities.ContainsKey(activityId))
        {
            throw new InvalidOperationException($"Aktivite zaten kayıtlı: {activityId}");
        }

        var metadata = new ActivityMetadata
        {
            ActivityId = activityId,
            DisplayName = activityId,
        };
        _activities[activityId] = metadata;
        return new ActivityBuilder(metadata);
    }

    /// <summary>Kaydedilen tüm aktiviteleri döndürür (immutable snapshot).</summary>
    public IReadOnlyDictionary<string, ActivityMetadata> Build() => _activities;

    /// <summary>Tek bir aktivitenin metadata'sını dolduran alt-builder.</summary>
    public sealed class ActivityBuilder
    {
        private readonly ActivityMetadata _metadata;

        internal ActivityBuilder(ActivityMetadata metadata) => _metadata = metadata;

        public ActivityBuilder DisplayName(string displayName)
        {
            _metadata.DisplayName = displayName;
            return this;
        }

        public ActivityBuilder Category(string category)
        {
            _metadata.Category = category;
            return this;
        }

        public ActivityBuilder Description(string description)
        {
            _metadata.Description = description;
            return this;
        }

        public ActivityBuilder Input(
            string name, string type, bool required = true,
            object? defaultValue = null, string? description = null, string? pickerKind = null,
            IEnumerable<string>? options = null)
        {
            _metadata.Inputs.Add(new ActivityParameter
            {
                Name = name,
                Type = type,
                Required = required,
                DefaultValue = defaultValue,
                Description = description,
                PickerKind = pickerKind,
                Options = options?.ToList(),
            });
            return this;
        }

        public ActivityBuilder Output(string name, string type, string? description = null)
        {
            _metadata.Outputs.Add(new ActivityParameter
            {
                Name = name,
                Type = type,
                Required = false,
                Description = description,
            });
            return this;
        }

        public ActivityBuilder DefaultProperty(string key, object? value)
        {
            _metadata.DefaultProperties[key] = value;
            return this;
        }

        public ActivityBuilder Capability(params string[] capabilities)
        {
            _metadata.RequiredCapabilities.AddRange(capabilities);
            return this;
        }

        public ActivityBuilder ExceptionClassification(string condition, ExceptionType classification)
        {
            _metadata.ExceptionClassification = new ExceptionClassificationRule
            {
                Condition = condition,
                Classification = classification,
            };
            return this;
        }
    }
}

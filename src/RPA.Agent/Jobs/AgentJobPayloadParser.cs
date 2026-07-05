namespace RPA.Agent.Jobs;

using System.Text.Json;
using RPA.Domain.Entities;

/// <summary>
/// Kuyruk kaleminin (QueueItem.Payload) JSON'ını çözerek çalıştırılabilir bir workflow sürümü
/// ve giriş argümanları üretir. Beklenen şema:
/// <code>
/// {
///   "workflowVersionId": "&lt;guid&gt;",
///   "version": "1.0.0",
///   "environmentId": "&lt;guid&gt;",
///   "jsonDefinition": { ...workflow node graph... },
///   "arguments": { "in_Musteri": "ACME" }
/// }
/// </code>
/// WorkflowVersion tablosu tam şemaya (WP-1.2) kavuşana dek payload, workflow tanımını
/// kendisi taşır; böylece ajanın Orchestrator'a ayrı bir workflow indirme çağrısına ihtiyacı olmaz.
/// </summary>
public static class AgentJobPayloadParser
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Payload'ı çözer. Geçersizse <see cref="FormatException"/> fırlatır.</summary>
    public static AgentJob Parse(Guid itemId, string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            throw new FormatException($"İş {itemId}: kuyruk payload'u boş.");

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(payload);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"İş {itemId}: kuyruk payload'u geçersiz JSON.", ex);
        }

        using (doc)
        {
            var root = doc.RootElement;

            var workflowVersion = new WorkflowVersion
            {
                Id = GetGuid(root, "workflowVersionId") ?? throw new FormatException(
                    $"İş {itemId}: payload 'workflowVersionId' içermiyor."),
                Version = GetString(root, "version") ?? "1.0.0",
                EnvironmentId = GetGuid(root, "environmentId") ?? Guid.Empty,
                JsonDefinition = GetDefinition(root),
            };

            var arguments = new Dictionary<string, object?>();
            if (root.TryGetProperty("arguments", out var args) && args.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in args.EnumerateObject())
                    arguments[prop.Name] = ToClrValue(prop.Value);
            }

            return new AgentJob(itemId, workflowVersion, arguments);
        }
    }

    private static string GetDefinition(JsonElement root)
    {
        if (!root.TryGetProperty("jsonDefinition", out var def))
            return "{}";
        // Nesne ise ham JSON'ını, string ise değerini al.
        return def.ValueKind == JsonValueKind.String ? def.GetString() ?? "{}" : def.GetRawText();
    }

    private static Guid? GetGuid(JsonElement root, string name)
        => root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
           && Guid.TryParse(el.GetString(), out var g) ? g : null;

    private static string? GetString(JsonElement root, string name)
        => root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
           ? el.GetString() : null;

    private static object? ToClrValue(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.TryGetInt64(out var l) ? l : (object)el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => el.GetRawText(),
    };
}

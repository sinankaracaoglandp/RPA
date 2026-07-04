namespace RPA.Infrastructure.Workflow;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RPA.Domain.Interfaces;

/// <summary>
/// Checkpoint durumunu serialize/deserialize eder (Task 2.5.1, Spec Bölüm 5.2, 2.6).
/// Format: <c>{ "nodeId": "...", "variables": { ... } }</c> — QueueItem.CheckpointData'da saklanır.
/// </summary>
public sealed class CheckpointManager : ICheckpointManager
{
    public string Serialize(string? lastCheckpointNodeId, IReadOnlyDictionary<string, object?> variables)
    {
        var payload = new JObject
        {
            ["nodeId"] = lastCheckpointNodeId,
            ["variables"] = JObject.FromObject(variables ?? new Dictionary<string, object?>()),
        };
        return payload.ToString(Formatting.None);
    }

    public CheckpointState? Deserialize(string? checkpointData)
    {
        if (string.IsNullOrWhiteSpace(checkpointData))
        {
            return null;
        }

        JObject obj;
        try
        {
            obj = JObject.Parse(checkpointData);
        }
        catch (JsonException ex)
        {
            throw new RPA.Domain.Exceptions.SystemException(
                $"Checkpoint verisi ayrıştırılamadı: {ex.Message}", ex);
        }

        var nodeId = (string?)obj["nodeId"];
        var variables = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (obj["variables"] is JObject varsObj)
        {
            foreach (var prop in varsObj.Properties())
            {
                variables[prop.Name] = VariableScope.JTokenToNative(prop.Value);
            }
        }

        return new CheckpointState(nodeId, variables);
    }
}

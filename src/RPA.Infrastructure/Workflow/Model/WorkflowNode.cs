namespace RPA.Infrastructure.Workflow.Model;

using Newtonsoft.Json.Linq;

/// <summary>
/// Tek bir workflow node'u — tüm node tiplerinin (activity, if, forEach, tryCatch, ...)
/// alanlarını taşıyan birleşik model. Kullanılmayan alanlar null kalır.
/// Spec Bölüm 5.1 node şeması.
/// </summary>
public sealed class WorkflowNode
{
    public string Id { get; init; } = "";
    public string Type { get; init; } = "";

    // activity
    public string? Activity { get; init; }
    public string? Channel { get; init; }
    public IReadOnlyDictionary<string, JToken?> Properties { get; init; }
        = new Dictionary<string, JToken?>();

    // assign
    public string? VariableName { get; init; }
    public JToken? Value { get; init; }

    // if / while
    public string? Condition { get; init; }

    // forEach
    public string? ItemVariable { get; init; }
    public string? Items { get; init; }

    // control-flow body
    public string? BodyStartNodeId { get; init; }
    public string? BodyEndNodeId { get; init; }

    // tryCatch
    public string? TryNodeId { get; init; }
    public string? CatchNodeId { get; init; }
    public string? FinallyNodeId { get; init; }
    public string? ExceptionVariable { get; init; }

    // log
    public string? Message { get; init; }
    public string? Level { get; init; }

    // delay
    public int? DurationMs { get; init; }

    // userPrompt
    public string? PromptTitle { get; init; }
    public string? PromptInputVariable { get; init; }
    public int? PromptTimeoutSeconds { get; init; }

    // terminate
    public string? ExceptionType { get; init; }

    // componentCall
    public string? ComponentId { get; init; }
    public string? ComponentVersion { get; init; }
    public IReadOnlyDictionary<string, string> InputMapping { get; init; }
        = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> OutputMapping { get; init; }
        = new Dictionary<string, string>();

    public static WorkflowNode FromJson(JObject n)
    {
        return new WorkflowNode
        {
            Id = (string?)n["id"] ?? "",
            Type = (string?)n["type"] ?? "",
            Activity = (string?)n["activity"],
            Channel = (string?)n["channel"],
            Properties = ReadTokenMap(n["properties"] as JObject),
            VariableName = (string?)n["variableName"],
            Value = n["value"],
            Condition = (string?)n["condition"],
            ItemVariable = (string?)n["itemVariable"],
            Items = (string?)n["items"],
            BodyStartNodeId = (string?)n["bodyStartNodeId"],
            BodyEndNodeId = (string?)n["bodyEndNodeId"],
            TryNodeId = (string?)n["tryNodeId"],
            CatchNodeId = (string?)n["catchNodeId"],
            FinallyNodeId = (string?)n["finallyNodeId"],
            ExceptionVariable = (string?)n["exceptionVariable"],
            Message = (string?)n["message"],
            Level = (string?)n["level"],
            DurationMs = (int?)n["durationMs"],
            PromptTitle = (string?)n["promptTitle"],
            PromptInputVariable = (string?)n["promptInputVariable"],
            PromptTimeoutSeconds = (int?)n["promptTimeoutSeconds"],
            ExceptionType = (string?)n["exceptionType"],
            ComponentId = (string?)n["componentId"],
            ComponentVersion = (string?)n["componentVersion"],
            InputMapping = ReadStringMap(n["inputMapping"] as JObject),
            OutputMapping = ReadStringMap(n["outputMapping"] as JObject),
        };
    }

    private static IReadOnlyDictionary<string, JToken?> ReadTokenMap(JObject? obj)
    {
        var map = new Dictionary<string, JToken?>(StringComparer.Ordinal);
        if (obj is not null)
        {
            foreach (var p in obj.Properties())
            {
                map[p.Name] = p.Value;
            }
        }
        return map;
    }

    private static IReadOnlyDictionary<string, string> ReadStringMap(JObject? obj)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (obj is not null)
        {
            foreach (var p in obj.Properties())
            {
                map[p.Name] = (string?)p.Value ?? "";
            }
        }
        return map;
    }
}

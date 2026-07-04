namespace RPA.Infrastructure.Workflow.Model;

using Newtonsoft.Json.Linq;

/// <summary>
/// Workflow JSON'ının çalıştırılabilir nesne modeli (Spec Bölüm 5.1 şeması).
/// <see cref="Parse"/> ile <c>JsonDefinition</c> string'inden üretilir.
/// Kontrat şeması (<c>WorkflowSchema.json</c>) değiştirilmez; bu tip yalnızca okur.
/// </summary>
public sealed class WorkflowDefinition
{
    public string SchemaVersion { get; init; } = "1.0";
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Version { get; init; } = "1.0.0";

    public List<WorkflowArgument> InputArguments { get; } = new();
    public List<WorkflowArgument> OutputArguments { get; } = new();
    public List<WorkflowVariable> Variables { get; } = new();
    public List<WorkflowNode> Nodes { get; } = new();
    public List<WorkflowConnection> Connections { get; } = new();

    /// <summary>Node ID → node hızlı erişim.</summary>
    public IReadOnlyDictionary<string, WorkflowNode> NodesById { get; private set; }
        = new Dictionary<string, WorkflowNode>();

    /// <summary>
    /// JSON içeriğini nesne modeline dönüştürür. Şema doğrulaması ayrı yapılır
    /// (<see cref="WorkflowValidator"/>); burada yalnızca ayrıştırma yapılır.
    /// </summary>
    public static WorkflowDefinition Parse(string json)
    {
        var root = JObject.Parse(json);

        var def = new WorkflowDefinition
        {
            SchemaVersion = (string?)root["schemaVersion"] ?? "1.0",
            Id = (string?)root["id"] ?? "",
            Name = (string?)root["name"] ?? "",
            Version = (string?)root["version"] ?? "1.0.0",
        };

        if (root["arguments"] is JObject args)
        {
            ReadArguments(args["in"] as JArray, def.InputArguments);
            ReadArguments(args["out"] as JArray, def.OutputArguments);
        }

        if (root["variables"] is JArray vars)
        {
            foreach (var v in vars.OfType<JObject>())
            {
                def.Variables.Add(new WorkflowVariable
                {
                    Name = (string?)v["name"] ?? "",
                    Type = (string?)v["type"] ?? "string",
                    Scope = (string?)v["scope"] ?? "local",
                    Default = v["default"],
                });
            }
        }

        if (root["nodes"] is JArray nodes)
        {
            foreach (var n in nodes.OfType<JObject>())
            {
                def.Nodes.Add(WorkflowNode.FromJson(n));
            }
        }

        if (root["connections"] is JArray conns)
        {
            foreach (var c in conns.OfType<JObject>())
            {
                def.Connections.Add(new WorkflowConnection
                {
                    From = (string?)c["from"] ?? "",
                    To = (string?)c["to"] ?? "",
                    FromPort = (string?)c["fromPort"] ?? "out",
                    Label = (string?)c["label"],
                });
            }
        }

        def.NodesById = def.Nodes
            .Where(n => !string.IsNullOrEmpty(n.Id))
            .GroupBy(n => n.Id)
            .ToDictionary(g => g.Key, g => g.First());

        return def;
    }

    private static void ReadArguments(JArray? arr, List<WorkflowArgument> target)
    {
        if (arr is null)
        {
            return;
        }
        foreach (var a in arr.OfType<JObject>())
        {
            target.Add(new WorkflowArgument
            {
                Name = (string?)a["name"] ?? "",
                Type = (string?)a["type"] ?? "string",
                Required = (bool?)a["required"] ?? false,
                Default = a["default"],
            });
        }
    }
}

public sealed class WorkflowArgument
{
    public string Name { get; init; } = "";
    public string Type { get; init; } = "string";
    public bool Required { get; init; }
    public JToken? Default { get; init; }
}

public sealed class WorkflowVariable
{
    public string Name { get; init; } = "";
    public string Type { get; init; } = "string";
    public string Scope { get; init; } = "local";
    public JToken? Default { get; init; }
}

public sealed class WorkflowConnection
{
    public string From { get; init; } = "";
    public string To { get; init; } = "";
    public string FromPort { get; init; } = "out";
    public string? Label { get; init; }
}

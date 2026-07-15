namespace RPA.Infrastructure.Workflow;

using System.Reflection;
using Newtonsoft.Json.Linq;
using NJsonSchema;
using NJsonSchema.Validation;

/// <summary>
/// Workflow JSON'ları Kontrat Paketi'ndeki <c>WorkflowSchema.json</c>'a göre doğrular.
/// Şema embedded resource olarak yüklenir (RPA.Domain/WorkflowSchema.json) ve
/// değiştirilmez referans olarak ele alınır (bkz. CLAUDE.md Kontrat Paketi).
/// Spec Bölüm 5.1.
/// </summary>
public sealed class WorkflowValidator
{
    // Şema tek sefer parse edilir (pahalı) ve thread-safe olarak paylaşılır.
    private static readonly Lazy<JsonSchema> _schema = new(LoadSchema, isThreadSafe: true);

    private const string ResourceName = "RPA.Infrastructure.Workflow.WorkflowSchema.json";

    /// <summary>
    /// Verilen JSON string'ini şemaya göre doğrular.
    /// </summary>
    /// <param name="workflowJson">Doğrulanacak workflow JSON içeriği.</param>
    /// <returns>Geçerli/geçersiz + hata listesi.</returns>
    public WorkflowValidationResult ValidateWorkflowJson(string workflowJson)
    {
        if (string.IsNullOrWhiteSpace(workflowJson))
        {
            return WorkflowValidationResult.Failure(new[] { "Workflow JSON boş olamaz." });
        }

        ICollection<ValidationError> errors;
        JObject workflow;
        try
        {
            workflow = JObject.Parse(workflowJson);
            errors = _schema.Value.Validate(workflow.ToString(Newtonsoft.Json.Formatting.None));
        }
        catch (Exception ex)
        {
            // Bozuk/parse edilemeyen JSON.
            return WorkflowValidationResult.Failure(
                new[] { $"JSON ayrıştırılamadı: {ex.Message}" });
        }

        var messages = new List<string>();
        Flatten(errors, messages);
        ValidateEInvoiceSources(workflow, messages);
        ValidateEInvoiceContracts(workflow, messages);

        if (messages.Count == 0)
        {
            return WorkflowValidationResult.Success();
        }

        return WorkflowValidationResult.Failure(messages);
    }

    private static void ValidateEInvoiceContracts(JObject workflow, List<string> messages)
    {
        if (workflow["nodes"] is not JArray nodes) return;
        var sources = new HashSet<string>(["Standard", "XPath", "InvoiceNotes", "LineNotes"]);
        var types = new HashSet<string>(["string", "decimal", "integer", "date", "boolean"]);
        for (var index = 0; index < nodes.Count; index++)
        {
            if (nodes[index] is not JObject node || !((string?)node["activity"] ?? "").StartsWith("EInvoice.ReadUbl", StringComparison.Ordinal)) continue;
            if (node["properties"] is not JObject properties) continue;
            if (properties["outputBindings"] is { Type: not JTokenType.Object and not JTokenType.Null }) messages.Add($"nodes[{index}].properties.outputBindings must be an object.");
            if ((string?)node["activity"] == "EInvoice.ReadUblBatch" && properties["errorMode"] is { Type: not JTokenType.Null } mode && (string?)mode is not ("Continue" or "Stop")) messages.Add($"nodes[{index}].properties.errorMode must be Continue or Stop.");
            if (properties["mappings"] is null) continue;
            if (properties["mappings"] is not JArray mappings) { messages.Add($"nodes[{index}].properties.mappings must be an array."); continue; }
            foreach (var token in mappings)
            {
                if (token is not JObject mapping) { messages.Add($"nodes[{index}].properties.mappings items must be objects."); continue; }
                var source = (string?)mapping["source"];
                if (string.IsNullOrWhiteSpace((string?)mapping["name"]) || source is null || !sources.Contains(source)) messages.Add($"nodes[{index}].properties.mappings has invalid name/source.");
                if (source == "XPath" && string.IsNullOrWhiteSpace((string?)mapping["valueXPath"])) messages.Add($"nodes[{index}].properties.mappings XPath requires valueXPath.");
                if (mapping["type"] is { } type && (type.Type != JTokenType.String || !types.Contains((string)type))) messages.Add($"nodes[{index}].properties.mappings has invalid type.");
                foreach (var flag in new[] { "required", "multiple" }) if (mapping[flag] is { Type: not JTokenType.Boolean and not JTokenType.Null }) messages.Add($"nodes[{index}].properties.mappings.{flag} must be boolean.");
            }
        }
    }

    private static void ValidateEInvoiceSources(JObject workflow, List<string> messages)
    {
        if (workflow["nodes"] is not JArray nodes) return;

        for (var index = 0; index < nodes.Count; index++)
        {
            if (nodes[index] is not JObject node || (string?)node["type"] != "activity") continue;

            var activity = (string?)node["activity"];
            var sourceNames = activity switch
            {
                "EInvoice.ReadUbl" => ("filePath", "xmlContent"),
                "EInvoice.ReadUblBatch" => ("filePaths", "xmlContents"),
                _ => default
            };
            if (sourceNames == default) continue;

            var properties = node["properties"] as JObject;
            var sourceCount = (HasSource(properties?[sourceNames.Item1]) ? 1 : 0)
                            + (HasSource(properties?[sourceNames.Item2]) ? 1 : 0);
            if (sourceCount != 1)
            {
                messages.Add($"nodes[{index}].properties: exactly one source ({sourceNames.Item1} or {sourceNames.Item2}) is required.");
            }
        }
    }

    private static bool HasSource(JToken? value) => value switch
    {
        null => false,
        { Type: JTokenType.Null or JTokenType.Undefined } => false,
        JValue { Type: JTokenType.String } text => !string.IsNullOrWhiteSpace((string?)text),
        JArray array => array.Any(HasSource),
        _ => true
    };

    /// <summary>
    /// NJsonSchema hata ağacını (child schema / item hataları dahil) düz listeye çevirir.
    /// </summary>
    private static void Flatten(IEnumerable<ValidationError> errors, List<string> messages)
    {
        foreach (var error in errors)
        {
            var path = string.IsNullOrEmpty(error.Path) ? "(kök)" : error.Path;
            messages.Add($"{path}: {error.Kind} ({error.Property})".Replace(" ()", ""));

            if (error is ChildSchemaValidationError childError)
            {
                foreach (var kvp in childError.Errors)
                {
                    Flatten(kvp.Value, messages);
                }
            }
        }
    }

    private static JsonSchema LoadSchema()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Gömülü şema kaynağı bulunamadı: {ResourceName}");
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();

        // Kontrat şeması AYNEN kullanılır (dosya değiştirilmez), ancak JSON Schema
        // ayrıştırıcıları için tolere edilemeyen bir tipografik hata içerir: bir
        // "properties" haritası içinde şema yerine düz string değere sahip bir giriş
        // (WorkflowSchema.json ~satır 94: yanlış konumlanmış "description"). Geçerli
        // JSON Schema'da "properties" değerleri yalnızca nesne/boolean (alt-şema)
        // olabilir. Bu tür geçersiz girişleri yalnızca bellekteki kopyadan ayıklarız;
        // dosyaya dokunulmaz ve doğrulama semantiği etkilenmez.
        var root = JObject.Parse(json);

        // Şema "$schema" beyanı içermiyor; "const" gibi draft-06+ anahtar kelimelerinin
        // (örn. schemaVersion const "1.0") uygulanması için taslak sürümü açıkça belirtilir.
        if (root["$schema"] is null)
        {
            root["$schema"] = "http://json-schema.org/draft-07/schema#";
        }

        SanitizePropertiesMaps(root);
        return JsonSchema.FromJsonAsync(root.ToString()).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Herhangi bir "properties" nesnesinde alt-şema olmayan (nesne/boolean dışı)
    /// değerleri özyinelemeli olarak kaldırır.
    /// </summary>
    private static void SanitizePropertiesMaps(JToken token)
    {
        switch (token)
        {
            case JObject obj:
                // NJsonSchema, draft-06+ "const" anahtar kelimesini yerel olarak
                // uygulamaz (ExtensionData'ya düşer). Tek elemanlı "enum"a çeviririz —
                // bu semantik olarak eşdeğerdir ve doğrulanır (örn. schemaVersion "1.0").
                if (obj["const"] is { } constValue && obj["enum"] is null)
                {
                    obj.Remove("const");
                    obj["enum"] = new JArray(constValue);
                }

                if (obj["properties"] is JObject props)
                {
                    foreach (var name in props.Properties().ToList())
                    {
                        if (name.Value.Type is not (JTokenType.Object or JTokenType.Boolean))
                        {
                            name.Remove();
                        }
                    }
                }
                foreach (var child in obj.Properties().ToList())
                {
                    SanitizePropertiesMaps(child.Value);
                }
                break;
            case JArray arr:
                foreach (var item in arr)
                {
                    SanitizePropertiesMaps(item);
                }
                break;
        }
    }
}

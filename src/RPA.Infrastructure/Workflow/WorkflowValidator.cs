namespace RPA.Infrastructure.Workflow;

using System.Reflection;
using Newtonsoft.Json.Linq;
using NJsonSchema;
using NJsonSchema.Validation;
using RPA.Infrastructure.Workflow.Activities.EInvoice;

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
        ValidateEInvoiceProfileContracts(workflow, messages);

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
            var mappingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (properties["mappings"] is not null && properties["mappings"] is not JArray)
                messages.Add($"nodes[{index}].properties.mappings must be an array.");
            foreach (var token in properties["mappings"] as JArray ?? [])
            {
                if (token is not JObject mapping) { messages.Add($"nodes[{index}].properties.mappings items must be objects."); continue; }
                var source = (string?)mapping["source"];
                if (!string.IsNullOrWhiteSpace((string?)mapping["name"])) mappingNames.Add((string)mapping["name"]!);
                if (string.IsNullOrWhiteSpace((string?)mapping["name"]) || source is null || !sources.Contains(source)) messages.Add($"nodes[{index}].properties.mappings has invalid name/source.");
                if (source == "XPath" && string.IsNullOrWhiteSpace((string?)mapping["valueXPath"])) messages.Add($"nodes[{index}].properties.mappings XPath requires valueXPath.");
                if (mapping["type"] is { } type &&
                    (type.Type != JTokenType.String || !types.Contains(type.Value<string>() ?? string.Empty)))
                    messages.Add($"nodes[{index}].properties.mappings has invalid type.");
                foreach (var flag in new[] { "required", "multiple" }) if (mapping[flag] is { Type: not JTokenType.Boolean and not JTokenType.Null }) messages.Add($"nodes[{index}].properties.mappings.{flag} must be boolean.");
            }
            if (properties["outputBindings"] is JObject bindings)
            {
                try { EInvoiceJson.ValidateOutputBindings(bindings, mappingNames); }
                catch (InvoiceParseException exception) { messages.Add($"nodes[{index}].properties.outputBindings: {exception.Message}"); }
            }
        }
    }

    private static void ValidateEInvoiceProfileContracts(JObject workflow, List<string> messages)
    {
        if (workflow["nodes"] is not JArray nodes) return;
        for (var index = 0; index < nodes.Count; index++)
        {
            if (nodes[index] is not JObject node) continue;
            var activity = (string?)node["activity"];
            if (activity is not ("EInvoice.ReadProfile" or "EInvoice.ReadProfileBatch")) continue;
            if (node["properties"] is not JObject properties)
            {
                messages.Add($"nodes[{index}].properties is required.");
                continue;
            }

            foreach (var required in new[] { "projectId", "profileId", "profileVersion", "sourceMode" })
                if (!HasSource(properties[required])) messages.Add($"nodes[{index}].properties.{required} is required.");
            if (properties["profileVersion"] is { } version && !IsPositiveInteger(version))
                messages.Add($"nodes[{index}].properties.profileVersion must be a positive integer.");
            if (properties["outputVariable"] is { Type: not JTokenType.Null } output && !IsIdentifier((string?)output))
                messages.Add($"nodes[{index}].properties.outputVariable must be a simple identifier.");

            var mode = (string?)properties["sourceMode"];
            if (activity == "EInvoice.ReadProfile")
            {
                if (mode is not ("FilePath" or "XmlContent"))
                    messages.Add($"nodes[{index}].properties.sourceMode must be FilePath or XmlContent.");
                ValidateSelectedSource(index, properties, mode, ["FilePath", "XmlContent"], ["filePath", "xmlContent"], messages);
            }
            else
            {
                if (mode is not ("Folder" or "FilePaths" or "XmlContents"))
                    messages.Add($"nodes[{index}].properties.sourceMode must be Folder, FilePaths or XmlContents.");
                ValidateSelectedSource(index, properties, mode, ["Folder", "FilePaths", "XmlContents"], ["folderPath", "filePaths", "xmlContents"], messages);
            }
        }
    }

    private static void ValidateSelectedSource(
        int nodeIndex,
        JObject properties,
        string? mode,
        string[] modes,
        string[] sourceNames,
        List<string> messages)
    {
        var sourceCount = sourceNames.Count(name => HasSource(properties[name]));
        if (sourceCount != 1)
        {
            messages.Add($"nodes[{nodeIndex}].properties: exactly one source ({string.Join(" or ", sourceNames)}) is required.");
            return;
        }

        var modeIndex = Array.IndexOf(modes, mode);
        if (modeIndex >= 0 && !HasSource(properties[sourceNames[modeIndex]]))
            messages.Add($"nodes[{nodeIndex}].properties.sourceMode must match the provided source.");
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

    private static bool IsPositiveInteger(JToken value) => value switch
    {
        JValue { Type: JTokenType.Integer } integer => integer.Value<long>() > 0,
        JValue { Type: JTokenType.String } text => int.TryParse((string?)text, out var number) && number > 0,
        _ => false
    };

    private static bool IsIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (!(value[0] == '_' || char.IsLetter(value[0]))) return false;
        for (var index = 1; index < value.Length; index++)
            if (value[index] != '_' && !char.IsLetterOrDigit(value[index])) return false;
        return true;
    }

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

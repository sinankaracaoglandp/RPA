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
        try
        {
            errors = _schema.Value.Validate(workflowJson);
        }
        catch (Exception ex)
        {
            // Bozuk/parse edilemeyen JSON.
            return WorkflowValidationResult.Failure(
                new[] { $"JSON ayrıştırılamadı: {ex.Message}" });
        }

        if (errors.Count == 0)
        {
            return WorkflowValidationResult.Success();
        }

        var messages = new List<string>();
        Flatten(errors, messages);
        return WorkflowValidationResult.Failure(messages);
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

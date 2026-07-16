namespace RPA.Application.EInvoiceProfiles;

using System.Text.Json;
using System.Text.RegularExpressions;
using RPA.Domain.Exceptions;

public sealed class EInvoiceProfileDefinitionValidator
{
    private static readonly Regex Identifier = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> Sources = new(StringComparer.Ordinal)
        { "Standard", "XPath", "InvoiceNotes", "LineNotes" };
    private static readonly Dictionary<string, string> JsonTypes = new(StringComparer.Ordinal)
        { ["string"] = "string", ["integer"] = "integer", ["decimal"] = "number", ["date"] = "string", ["boolean"] = "boolean" };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public EInvoiceProfileDefinition ParseAndValidate(string definitionJson)
    {
        EInvoiceProfileDefinition definition;
        try
        {
            definition = JsonSerializer.Deserialize<EInvoiceProfileDefinition>(definitionJson, JsonOptions)
                ?? throw new BusinessException("E-fatura profil tanımı boş olamaz.");
        }
        catch (JsonException exception)
        {
            throw new BusinessException("E-fatura profil tanımı geçerli JSON olmalıdır.", exception);
        }

        var rootNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in definition.Fields)
        {
            ValidateField(field);
            AddUnique(rootNames, field.Name, "Kök alan");
        }
        foreach (var collection in definition.Collections)
        {
            ValidateName(collection.Name, "Koleksiyon");
            AddUnique(rootNames, collection.Name, "Kök alan/koleksiyon");
            if (string.IsNullOrWhiteSpace(collection.ScopeXPath))
                throw new BusinessException($"Koleksiyon scopeXPath zorunludur: {collection.Name}");
            var childNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in collection.Fields)
            {
                ValidateField(field);
                AddUnique(childNames, field.Name, $"{collection.Name} alanı");
            }
        }
        return definition;
    }

    public string ValidateAndBuildSchema(string definitionJson)
    {
        var definition = ParseAndValidate(definitionJson);
        var properties = new Dictionary<string, object?>();
        var required = new List<string>();
        foreach (var field in definition.Fields)
        {
            properties[field.Name] = FieldSchema(field);
            if (field.Required) required.Add(field.Name);
        }
        foreach (var collection in definition.Collections)
        {
            var itemProperties = collection.Fields.ToDictionary(x => x.Name, x => (object?)FieldSchema(x));
            var itemRequired = collection.Fields.Where(x => x.Required).Select(x => x.Name).ToArray();
            properties[collection.Name] = new Dictionary<string, object?>
            {
                ["type"] = "array",
                ["items"] = ObjectSchema(itemProperties, itemRequired),
            };
        }
        return JsonSerializer.Serialize(ObjectSchema(properties, required));
    }

    private static Dictionary<string, object?> ObjectSchema(Dictionary<string, object?> properties, IEnumerable<string> required) => new()
    {
        ["type"] = "object",
        ["properties"] = properties,
        ["required"] = required.ToArray(),
        ["additionalProperties"] = false,
    };

    private static Dictionary<string, object?> FieldSchema(EInvoiceFieldDefinition field)
    {
        var schema = new Dictionary<string, object?> { ["type"] = JsonTypes[field.Type] };
        if (field.Type == "date") schema["format"] = "date";
        return schema;
    }

    private static void ValidateField(EInvoiceFieldDefinition field)
    {
        ValidateName(field.Name, "Alan");
        if (!Sources.Contains(field.Source)) throw new BusinessException($"Desteklenmeyen alan kaynağı: {field.Name}");
        if (!JsonTypes.ContainsKey(field.Type)) throw new BusinessException($"Desteklenmeyen alan tipi: {field.Name}");
        if (field.Source == "XPath" && string.IsNullOrWhiteSpace(field.ValueXPath))
            throw new BusinessException($"XPath alanında valueXPath zorunludur: {field.Name}");
    }

    private static void ValidateName(string name, string kind)
    {
        if (string.IsNullOrWhiteSpace(name) || !Identifier.IsMatch(name))
            throw new BusinessException($"{kind} adı geçerli bir identifier olmalıdır: {name}");
    }

    private static void AddUnique(HashSet<string> names, string name, string kind)
    {
        if (!names.Add(name)) throw new BusinessException($"{kind} adı tekrar ediyor: {name}");
    }
}

namespace RPA.Infrastructure.Workflow.Activities.EInvoice;

using System.Text.Json;
using RPA.Application.EInvoiceProfiles;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Services;

public sealed class ReadProfileActivity(EInvoiceProfileService profiles, EInvoiceProfileExtractor extractor) : IActivity
{
    public ActivityMetadata GetMetadata() => EInvoiceProfileActivityMetadata.ReadProfile();

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var definition = await ReadDefinitionAsync(context);
        var xml = ReadSingleXml(context);
        var invoice = extractor.Extract(xml, definition);
        var outputVariable = ReadOutputVariable(context, "fatura");
        context.SetVariable(outputVariable, invoice);
        return new Dictionary<string, object?> { [outputVariable] = invoice };
    }

    private async Task<EInvoiceProfileDefinition> ReadDefinitionAsync(IActivityExecutionContext context)
    {
        var version = await profiles.GetVersionAsync(
            ReadGuid(context, "projectId"),
            ReadGuid(context, "profileId"),
            ReadPositiveInt(context, "profileVersion"));
        return DeserializeDefinition(version.DefinitionJson);
    }

    private static string ReadSingleXml(IActivityExecutionContext context)
    {
        var mode = ReadMode(context, new HashSet<string>(StringComparer.Ordinal) { "FilePath", "XmlContent" });
        var filePath = context.GetVariable<string?>("filePath");
        var xmlContent = context.GetVariable<string?>("xmlContent");
        var count = HasText(filePath) ? 1 : 0;
        count += HasText(xmlContent) ? 1 : 0;
        if (count != 1)
            throw new InvoiceParseException("filePath veya xmlContent alanlarindan tam olarak biri saglanmalidir.");
        return mode switch
        {
            "FilePath" when HasText(filePath) => File.ReadAllText(filePath!),
            "XmlContent" when HasText(xmlContent) => xmlContent!,
            _ => throw new InvoiceParseException("sourceMode secilen kaynak alaniyla uyumlu olmalidir.")
        };
    }

    internal static EInvoiceProfileDefinition DeserializeDefinition(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<EInvoiceProfileDefinition>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? throw new InvoiceParseException("E-fatura profil tanimi bos.");
        }
        catch (JsonException)
        {
            throw new InvoiceParseException("E-fatura profil tanimi gecersiz.");
        }
    }

    internal static Guid ReadGuid(IActivityExecutionContext context, string name)
    {
        var value = context.GetVariable<object?>(name);
        if (value is Guid guid && guid != Guid.Empty) return guid;
        if (value is string text && Guid.TryParse(text, out guid) && guid != Guid.Empty) return guid;
        throw new InvoiceParseException($"{name} zorunlu ve gecerli bir GUID olmalidir.");
    }

    internal static int ReadPositiveInt(IActivityExecutionContext context, string name)
    {
        var value = context.GetVariable<object?>(name);
        var number = value switch
        {
            int i => i,
            long l when l <= int.MaxValue => (int)l,
            string text when int.TryParse(text, out var parsed) => parsed,
            _ => 0
        };
        if (number <= 0) throw new InvoiceParseException($"{name} pozitif olmalidir.");
        return number;
    }

    internal static string ReadOutputVariable(IActivityExecutionContext context, string fallback)
    {
        var value = context.GetVariable<string?>("outputVariable")?.Trim();
        return HasText(value) ? value! : fallback;
    }

    internal static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);

    internal static string ReadMode(IActivityExecutionContext context, IReadOnlySet<string> allowed)
    {
        var mode = context.GetVariable<string?>("sourceMode")?.Trim();
        if (mode is null || !allowed.Contains(mode))
            throw new InvoiceParseException($"sourceMode su degerlerden biri olmalidir: {string.Join(", ", allowed)}.");
        return mode;
    }
}

public sealed class ReadProfileBatchActivity(EInvoiceProfileService profiles, EInvoiceProfileExtractor extractor) : IActivity
{
    public ActivityMetadata GetMetadata() => EInvoiceProfileActivityMetadata.ReadProfileBatch();

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var version = await profiles.GetVersionAsync(
            ReadProfileActivity.ReadGuid(context, "projectId"),
            ReadProfileActivity.ReadGuid(context, "profileId"),
            ReadProfileActivity.ReadPositiveInt(context, "profileVersion"));
        var definition = ReadProfileActivity.DeserializeDefinition(version.DefinitionJson);
        var invoices = ReadBatchXml(context).Select(xml => extractor.Extract(xml, definition)).ToList();
        var outputVariable = ReadProfileActivity.ReadOutputVariable(context, "faturalar");
        context.SetVariable(outputVariable, invoices);
        return new Dictionary<string, object?> { [outputVariable] = invoices };
    }

    private static IEnumerable<string> ReadBatchXml(IActivityExecutionContext context)
    {
        var mode = ReadProfileActivity.ReadMode(context, new HashSet<string>(StringComparer.Ordinal)
            { "Folder", "FilePaths", "XmlContents" });
        var filePaths = EInvoiceJson.ReadStrings(context.GetVariable<object?>("filePaths"));
        var xmlContents = EInvoiceJson.ReadStrings(context.GetVariable<object?>("xmlContents"));
        var folderPath = context.GetVariable<string?>("folderPath");
        var sourceCount = ReadProfileActivity.HasText(folderPath) ? 1 : 0;
        sourceCount += filePaths.Count > 0 ? 1 : 0;
        sourceCount += xmlContents.Count > 0 ? 1 : 0;
        if (sourceCount != 1)
            throw new InvoiceParseException("folderPath, filePaths veya xmlContents alanlarindan tam olarak biri saglanmalidir.");

        return mode switch
        {
            "Folder" when ReadProfileActivity.HasText(folderPath) => ReadFolder(folderPath!, context),
            "FilePaths" when filePaths.Count > 0 => filePaths.Select(File.ReadAllText),
            "XmlContents" when xmlContents.Count > 0 => xmlContents,
            _ => throw new InvoiceParseException("sourceMode secilen kaynak alaniyla uyumlu olmalidir.")
        };
    }

    private static IEnumerable<string> ReadFolder(string folderPath, IActivityExecutionContext context)
    {
        if (!Directory.Exists(folderPath)) throw new InvoiceParseException("E-fatura klasoru bulunamadi.");
        var filter = context.GetVariable<string?>("fileFilter");
        if (string.IsNullOrWhiteSpace(filter)) filter = "*.xml";
        var includeSubfolders = context.GetVariable<bool?>("includeSubfolders") ?? false;
        var option = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return Directory.EnumerateFiles(folderPath, filter, option)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(File.ReadAllText)
            .ToList();
    }

}

internal static class EInvoiceProfileActivityMetadata
{
    public static ActivityMetadata ReadProfile() => new()
    {
        ActivityId = "EInvoice.ReadProfile", DisplayName = "E-Fatura Profili Oku", Category = "E-Fatura",
        Inputs =
        [
            new() { Name = "projectId", Type = "string", Required = true },
            new() { Name = "profileId", Type = "string", Required = true, PickerKind = "einvoice-profile" },
            new() { Name = "profileVersion", Type = "int", Required = true },
            new() { Name = "sourceMode", Type = "string", Required = true, Options = ["FilePath", "XmlContent"], DefaultValue = "XmlContent" },
            new() { Name = "filePath", Type = "Sensitive", Required = false },
            new() { Name = "xmlContent", Type = "Sensitive", Required = false },
            new() { Name = "outputVariable", Type = "string", Required = false, DefaultValue = "fatura" }
        ],
        Outputs = [new() { Name = "fatura", Type = "JSON" }]
    };

    public static ActivityMetadata ReadProfileBatch() => new()
    {
        ActivityId = "EInvoice.ReadProfileBatch", DisplayName = "E-Fatura Profili Toplu Oku", Category = "E-Fatura",
        Inputs =
        [
            new() { Name = "projectId", Type = "string", Required = true },
            new() { Name = "profileId", Type = "string", Required = true, PickerKind = "einvoice-profile" },
            new() { Name = "profileVersion", Type = "int", Required = true },
            new() { Name = "sourceMode", Type = "string", Required = true, Options = ["Folder", "FilePaths", "XmlContents"], DefaultValue = "Folder" },
            new() { Name = "folderPath", Type = "Sensitive", Required = false },
            new() { Name = "fileFilter", Type = "string", Required = false, DefaultValue = "*.xml" },
            new() { Name = "includeSubfolders", Type = "bool", Required = false, DefaultValue = false },
            new() { Name = "filePaths", Type = "Sensitive", Required = false },
            new() { Name = "xmlContents", Type = "Sensitive", Required = false },
            new() { Name = "outputVariable", Type = "string", Required = false, DefaultValue = "faturalar" }
        ],
        Outputs = [new() { Name = "faturalar", Type = "JSON" }]
    };
}

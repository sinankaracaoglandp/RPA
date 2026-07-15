namespace RPA.Infrastructure.Workflow.Activities.EInvoice;

using System.Text.Json;
using RPA.Domain.Interfaces;

public sealed class ReadUblActivity(UblInvoiceParser parser) : IActivity
{
    public ActivityMetadata GetMetadata() => EInvoiceActivityMetadata.ReadUbl();

    public Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var path = context.GetVariable<string?>("filePath");
        var xml = context.GetVariable<string?>("xmlContent");
        if (string.IsNullOrWhiteSpace(path) == string.IsNullOrWhiteSpace(xml))
            throw new InvoiceParseException("filePath veya xmlContent alanlarından tam olarak biri sağlanmalıdır.");

        var mappings = EInvoiceJson.ReadMappings(context.GetVariable<object?>("mappings"));
        var invoice = !string.IsNullOrWhiteSpace(xml) ? parser.Parse(xml, mappings) : parser.ParseFile(path!, mappings);
        context.SetVariable("invoice", invoice);
        context.SetVariable("lines", invoice.Lines);
        context.SetVariable("customFields", invoice.CustomFields);
        EInvoiceJson.ApplyOutputBindings(context, invoice, context.GetVariable<object?>("outputBindings"));

        return Task.FromResult(new Dictionary<string, object?>
        {
            ["invoice"] = invoice,
            ["lines"] = invoice.Lines,
            ["customFields"] = invoice.CustomFields
        });
    }
}

public sealed class ReadUblBatchActivity(UblInvoiceParser parser) : IActivity
{
    public ActivityMetadata GetMetadata() => EInvoiceActivityMetadata.ReadUblBatch();

    public Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var filePaths = EInvoiceJson.ReadStrings(context.GetVariable<object?>("filePaths"));
        var xmlContents = EInvoiceJson.ReadStrings(context.GetVariable<object?>("xmlContents"));
        if ((filePaths.Count > 0) == (xmlContents.Count > 0))
            throw new InvoiceParseException("filePaths veya xmlContents alanlarından tam olarak biri sağlanmalıdır.");

        var mappings = EInvoiceJson.ReadMappings(context.GetVariable<object?>("mappings"));
        var stopOnError = ReadStopOnError(context.GetVariable<string?>("errorMode"));
        var sources = filePaths.Count > 0 ? filePaths : xmlContents;
        var results = new List<InvoiceBatchItemResult>(sources.Count);
        for (var index = 0; index < sources.Count; index++)
        {
            try
            {
                var invoice = filePaths.Count > 0
                    ? parser.ParseFile(sources[index], mappings)
                    : parser.Parse(sources[index], mappings);
                results.Add(new InvoiceBatchItemResult(index, true, invoice, null));
            }
            catch (Exception exception) when (exception is InvoiceParseException or IOException or UnauthorizedAccessException)
            {
                if (stopOnError)
                    throw exception is InvoiceParseException parseException
                        ? parseException
                        : new InvoiceParseException($"{index}. kaynak işlenemedi.");
                results.Add(new InvoiceBatchItemResult(index, false, null, $"{index}. kaynak işlenemedi: geçersiz e-fatura verisi."));
            }
        }

        context.SetVariable("results", results);
        return Task.FromResult(new Dictionary<string, object?> { ["results"] = results });
    }

    private static bool ReadStopOnError(string? errorMode) => errorMode?.Trim() switch
    {
        null or "" or "Stop" => true,
        "Continue" => false,
        _ => throw new InvoiceParseException("errorMode yalnızca Stop veya Continue olabilir.")
    };
}

internal static class EInvoiceJson
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<InvoiceMappingRule> ReadMappings(object? value) => value switch
    {
        null => [],
        IReadOnlyList<InvoiceMappingRule> rules => rules,
        IEnumerable<InvoiceMappingRule> rules => rules.ToList(),
        JsonElement element => DeserializeMappings(element.GetRawText()),
        string json when string.IsNullOrWhiteSpace(json) => [],
        string json => DeserializeMappings(json),
        _ => throw new InvoiceParseException("mappings geçerli bir JSON dizisi olmalıdır.")
    };

    public static List<string> ReadStrings(object? value) => value switch
    {
        null => [],
        string json when string.IsNullOrWhiteSpace(json) => [],
        string json => DeserializeStrings(json),
        JsonElement element => DeserializeStrings(element.GetRawText()),
        IEnumerable<string> values => values.Where(item => !string.IsNullOrWhiteSpace(item)).ToList(),
        _ => throw new InvoiceParseException("Batch kaynağı bir string koleksiyonu olmalıdır.")
    };

    public static void ApplyOutputBindings(IActivityExecutionContext context, InvoiceData invoice, object? value)
    {
        foreach (var (source, target) in ReadBindings(value))
        {
            if (string.IsNullOrWhiteSpace(target)) throw new InvoiceParseException("Output binding hedefi boş olamaz.");
            if (!TryReadBindableValue(invoice, source, out var output))
                throw new InvoiceParseException($"Output binding alanına izin verilmiyor: {source}");
            context.SetVariable(target, output);
        }
    }

    private static bool TryReadBindableValue(InvoiceData invoice, string source, out object? value)
    {
        if (invoice.CustomFields.TryGetValue(source, out value)) return true;
        value = source.ToLowerInvariant() switch
        {
            "uuid" => invoice.Uuid,
            "invoicenumber" => invoice.InvoiceNumber,
            "issuedate" => invoice.IssueDate,
            "invoicetype" => invoice.InvoiceType,
            "profileid" => invoice.ProfileId,
            "currency" => invoice.Currency,
            "supplier" => invoice.Supplier,
            "customer" => invoice.Customer,
            "lines" => invoice.Lines,
            "notes" => invoice.Notes,
            "exchangerate" => invoice.ExchangeRate,
            "paymentaccounts" => invoice.PaymentAccounts,
            "taxexclusiveamount" => invoice.TaxExclusiveAmount,
            "taxinclusiveamount" => invoice.TaxInclusiveAmount,
            "payableamount" => invoice.PayableAmount,
            "customfields" => invoice.CustomFields,
            "extractionsources" => invoice.ExtractionSources,
            _ => null
        };
        return BindableProperties.Contains(source);
    }

    private static readonly HashSet<string> BindableProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "Uuid", "InvoiceNumber", "IssueDate", "InvoiceType", "ProfileId", "Currency", "Supplier", "Customer",
        "Lines", "Notes", "ExchangeRate", "PaymentAccounts", "TaxExclusiveAmount", "TaxInclusiveAmount",
        "PayableAmount", "CustomFields", "ExtractionSources"
    };

    private static IReadOnlyDictionary<string, string> ReadBindings(object? value) => value switch
    {
        null => new Dictionary<string, string>(),
        IReadOnlyDictionary<string, string> bindings => bindings,
        JsonElement element => DeserializeBindings(element.GetRawText()),
        string json when string.IsNullOrWhiteSpace(json) => new Dictionary<string, string>(),
        string json => DeserializeBindings(json),
        _ => throw new InvoiceParseException("outputBindings geçerli bir JSON nesnesi olmalıdır.")
    };

    private static List<InvoiceMappingRule> DeserializeMappings(string json) => Deserialize<List<InvoiceMappingRule>>(json, "mappings") ?? [];
    private static List<string> DeserializeStrings(string json) => Deserialize<List<string>>(json, "batch kaynağı") ?? [];
    private static Dictionary<string, string> DeserializeBindings(string json) => Deserialize<Dictionary<string, string>>(json, "outputBindings") ?? [];

    private static T? Deserialize<T>(string json, string field)
    {
        try { return JsonSerializer.Deserialize<T>(json, JsonOptions); }
        catch (JsonException) { throw new InvoiceParseException($"{field} geçerli JSON içermiyor."); }
    }
}

internal static class EInvoiceActivityMetadata
{
    public static ActivityMetadata ReadUbl() => new()
    {
        ActivityId = "EInvoice.ReadUbl", DisplayName = "UBL Fatura Oku", Category = "E-Fatura",
        Inputs = [new() { Name = "filePath", Type = "string", Required = false }, new() { Name = "xmlContent", Type = "string", Required = false }],
        Outputs = [new() { Name = "invoice", Type = "JSON" }, new() { Name = "lines", Type = "JSON" }, new() { Name = "customFields", Type = "JSON" }]
    };

    public static ActivityMetadata ReadUblBatch() => new()
    {
        ActivityId = "EInvoice.ReadUblBatch", DisplayName = "UBL Faturaları Toplu Oku", Category = "E-Fatura",
        Inputs = [new() { Name = "filePaths", Type = "JSON", Required = false }, new() { Name = "xmlContents", Type = "JSON", Required = false }],
        Outputs = [new() { Name = "results", Type = "JSON" }]
    };
}

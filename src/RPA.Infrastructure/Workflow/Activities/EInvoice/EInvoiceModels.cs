namespace RPA.Infrastructure.Workflow.Activities.EInvoice;

public sealed class InvoiceData
{
    public string? Uuid { get; init; }
    public string? InvoiceNumber { get; init; }
    public DateOnly? IssueDate { get; init; }
    public string? InvoiceType { get; init; }
    public string? ProfileId { get; init; }
    public string? Currency { get; init; }
    public InvoicePartyData? Supplier { get; init; }
    public InvoicePartyData? Customer { get; init; }
    public List<InvoiceLineData> Lines { get; init; } = [];
    public List<string> Notes { get; init; } = [];
    public decimal? ExchangeRate { get; set; }
    public List<string> PaymentAccounts { get; init; } = [];
    public decimal? TaxExclusiveAmount { get; init; }
    public decimal? TaxInclusiveAmount { get; init; }
    public decimal? PayableAmount { get; init; }
    public Dictionary<string, object?> CustomFields { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class InvoicePartyData
{
    public string? Name { get; init; }
    public string? TaxId { get; init; }
    public string? TaxOffice { get; init; }
}

public sealed class InvoiceLineData
{
    public string? Id { get; init; }
    public string? ItemCode { get; init; }
    public string? Name { get; init; }
    public decimal? Quantity { get; init; }
    public string? UnitCode { get; init; }
    public decimal? UnitPrice { get; init; }
    public decimal? LineExtensionAmount { get; init; }
    public List<string> Notes { get; init; } = [];
}

public sealed record InvoiceTaxData(string? Code, string? Name, decimal? Percent, decimal? Amount);

public sealed record InvoiceMappingRule(
    string Name,
    string Source,
    string? ScopeXPath,
    string? ValueXPath,
    string? Regex,
    string? Group,
    string Type = "string",
    bool Required = false,
    bool Multiple = false);

public sealed record InvoiceBatchItemResult(int SourceIndex, bool Success, InvoiceData? Invoice, string? Error);

public sealed record InvoiceParseOptions(int MaxCharacters = 10 * 1024 * 1024, TimeSpan? RegexTimeout = null)
{
    public TimeSpan EffectiveRegexTimeout => RegexTimeout ?? TimeSpan.FromMilliseconds(500);
}

public sealed class InvoiceParseException(string message) : Exception(message);

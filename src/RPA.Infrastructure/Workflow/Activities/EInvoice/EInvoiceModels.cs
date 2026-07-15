namespace RPA.Infrastructure.Workflow.Activities.EInvoice;

public sealed class InvoiceData
{
    public string? Uuid { get; init; }
    public string? InvoiceNumber { get; init; }
    public DateOnly? IssueDate { get; init; }
    public TimeOnly? IssueTime { get; init; }
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
    public decimal? TaxAmount { get; init; }
    public decimal? AllowanceTotalAmount { get; init; }
    public decimal? PayableAmount { get; init; }
    public List<InvoiceTaxData> Taxes { get; init; } = [];
    public List<InvoiceTaxData> WithholdingTaxes { get; init; } = [];
    public Dictionary<string, object?> CustomFields { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> ExtractionSources { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class InvoicePartyData
{
    public string? Name { get; init; }
    public string? TaxId { get; init; }
    public string? TaxOffice { get; init; }
    public InvoiceAddressData? Address { get; init; }
    public InvoiceContactData? Contact { get; init; }
}

public sealed class InvoiceAddressData
{
    public string? StreetName { get; init; }
    public string? CitySubdivisionName { get; init; }
    public string? CityName { get; init; }
    public string? PostalZone { get; init; }
    public string? CountryName { get; init; }
}

public sealed class InvoiceContactData
{
    public string? Name { get; init; }
    public string? Telephone { get; init; }
    public string? Email { get; init; }
}

public sealed class InvoiceLineData
{
    public string? Id { get; init; }
    public string? ItemCode { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }
    public decimal? Quantity { get; init; }
    public string? UnitCode { get; init; }
    public decimal? UnitPrice { get; init; }
    public decimal? LineExtensionAmount { get; init; }
    public decimal? DiscountAmount { get; init; }
    public List<InvoiceTaxData> Taxes { get; init; } = [];
    public List<InvoiceTaxData> WithholdingTaxes { get; init; } = [];
    public List<string> Notes { get; init; } = [];
}

public sealed record InvoiceTaxData(
    string? Code,
    string? Name,
    decimal? Percent,
    decimal? Amount,
    string? ExemptionReasonCode = null,
    string? ExemptionReason = null,
    bool IsWithholding = false);

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

public sealed record InvoiceParseOptions(int MaxCharacters = 10 * 1024 * 1024, TimeSpan? RegexTimeout = null, int MaxDepth = 128)
{
    public TimeSpan EffectiveRegexTimeout => RegexTimeout ?? TimeSpan.FromMilliseconds(500);
}

public sealed class InvoiceParseException(string message) : Exception(message);

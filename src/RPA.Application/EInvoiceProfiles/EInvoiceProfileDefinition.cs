namespace RPA.Application.EInvoiceProfiles;

public sealed class EInvoiceProfileDefinition
{
    public List<EInvoiceFieldDefinition> Fields { get; init; } = [];
    public List<EInvoiceCollectionDefinition> Collections { get; init; } = [];
}

public sealed class EInvoiceCollectionDefinition
{
    public string Name { get; init; } = string.Empty;
    public string ScopeXPath { get; init; } = string.Empty;
    public List<EInvoiceFieldDefinition> Fields { get; init; } = [];
}

public sealed class EInvoiceFieldDefinition
{
    public string Name { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string? ValueXPath { get; init; }
    public string? Regex { get; init; }
    public string? Group { get; init; }
    public string Type { get; init; } = "string";
    public bool Required { get; init; }
    public bool Multiple { get; init; }
}

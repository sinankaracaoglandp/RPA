namespace RPA.Application.Tests.EInvoiceProfiles;

using System.Text.Json;
using RPA.Application.EInvoiceProfiles;
using RPA.Domain.Exceptions;

public sealed class EInvoiceProfileDefinitionValidatorTests
{
    [Fact]
    public void BuildsObjectSchema_WithTypedCollectionItems()
    {
        const string json = """
            {"fields":[{"name":"faturaNo","source":"XPath","valueXPath":"/Invoice/ID","type":"string"}],
             "collections":[{"name":"satirlar","scopeXPath":"/Invoice/InvoiceLine","fields":[{"name":"Miktar","source":"XPath","valueXPath":"./Quantity","type":"decimal"}]}]}
            """;

        using var schema = JsonDocument.Parse(new EInvoiceProfileDefinitionValidator().ValidateAndBuildSchema(json));

        Assert.Equal("object", schema.RootElement.GetProperty("type").GetString());
        var collection = schema.RootElement.GetProperty("properties").GetProperty("satirlar");
        Assert.Equal("array", collection.GetProperty("type").GetString());
        Assert.Equal("number", collection.GetProperty("items").GetProperty("properties").GetProperty("Miktar").GetProperty("type").GetString());
    }

    [Theory]
    [InlineData("FaturaNo", "faturano")]
    [InlineData("satirlar", "SATIRLAR")]
    public void RejectsCaseInsensitiveDuplicateRootNames(string first, string second)
    {
        var json = $$"""
            {"fields":[{"name":"{{first}}","source":"XPath","valueXPath":"/Invoice/ID","type":"string"},
                       {"name":"{{second}}","source":"XPath","valueXPath":"/Invoice/IssueDate","type":"date"}],"collections":[]}
            """;

        Assert.Throws<BusinessException>(() => new EInvoiceProfileDefinitionValidator().ValidateAndBuildSchema(json));
    }

    [Fact]
    public void ParseAndValidate_FallbackGroupWithoutFallbackRegex_Throws()
    {
        var json = """{"fields":[{"name":"iban","source":"XPath","valueXPath":"//cbc:ID","fallbackGroup":"deger","type":"string"}],"collections":[]}""";
        var validator = new EInvoiceProfileDefinitionValidator();
        var exception = Assert.Throws<BusinessException>(() => validator.ParseAndValidate(json));
        Assert.Contains("fallbackRegex", exception.Message);
    }

    [Fact]
    public void ParseAndValidate_InvalidFallbackRegexPattern_Throws()
    {
        var json = """{"fields":[{"name":"iban","source":"XPath","valueXPath":"//cbc:ID","fallbackRegex":"[","type":"string"}],"collections":[]}""";
        var validator = new EInvoiceProfileDefinitionValidator();
        var exception = Assert.Throws<BusinessException>(() => validator.ParseAndValidate(json));
        Assert.Contains("fallback regex", exception.Message);
    }

    [Fact]
    public void ParseAndValidate_ValidFallbackRegex_RoundTrips()
    {
        var json = """{"fields":[{"name":"iban","source":"XPath","valueXPath":"//cbc:ID","fallbackRegex":"TR\\d{24}","fallbackGroup":null,"type":"string"}],"collections":[]}""";
        var definition = new EInvoiceProfileDefinitionValidator().ParseAndValidate(json);
        Assert.Equal("TR\\d{24}", definition.Fields[0].FallbackRegex);
    }
}

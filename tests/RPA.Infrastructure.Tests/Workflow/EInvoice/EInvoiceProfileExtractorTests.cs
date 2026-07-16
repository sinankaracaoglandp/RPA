namespace RPA.Infrastructure.Tests.Workflow.EInvoice;

using RPA.Application.EInvoiceProfiles;
using RPA.Infrastructure.Workflow.Activities.EInvoice;

public sealed class EInvoiceProfileExtractorTests
{
    private const string Xml = """
        <Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"
                 xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2"
                 xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2">
          <cbc:ID>FAT-42</cbc:ID><cbc:Note>Sipariş No: SIP-9</cbc:Note>
          <cac:InvoiceLine><cbc:ID>1</cbc:ID><cbc:InvoicedQuantity>2.5</cbc:InvoicedQuantity><cac:Item><cbc:Name>Kalem</cbc:Name></cac:Item></cac:InvoiceLine>
        </Invoice>
        """;

    [Fact]
    public void Extract_BuildsDynamicRootAndCollections_WithRegexAndTypes()
    {
        var definition = new EInvoiceProfileDefinition
        {
            Fields =
            [
                new() { Name = "faturaNo", Source = "XPath", ValueXPath = "/inv:Invoice/cbc:ID" },
                new() { Name = "siparisNo", Source = "InvoiceNotes", Regex = @"Sipariş No:\s*(?<value>\S+)", Group = "value" }
            ],
            Collections =
            [
                new()
                {
                    Name = "satirlar", ScopeXPath = "//cac:InvoiceLine", Fields =
                    [
                        new() { Name = "Aciklama", Source = "XPath", ValueXPath = "cac:Item/cbc:Name" },
                        new() { Name = "Miktar", Source = "XPath", ValueXPath = "cbc:InvoicedQuantity", Type = "decimal" }
                    ]
                }
            ]
        };

        var result = new EInvoiceProfileExtractor().Extract(Xml, definition);

        Assert.Equal("FAT-42", result["faturaNo"]);
        Assert.Equal("SIP-9", result["siparisNo"]);
        var line = Assert.Single(Assert.IsType<List<Dictionary<string, object?>>>(result["satirlar"]));
        Assert.Equal("Kalem", line["Aciklama"]);
        Assert.Equal(2.5m, line["Miktar"]);
    }

    [Fact]
    public void Extract_RejectsDtdAndRequiredMissingField()
    {
        var extractor = new EInvoiceProfileExtractor();
        var definition = new EInvoiceProfileDefinition { Fields = [new() { Name = "x", Source = "XPath", ValueXPath = "//cbc:Missing", Required = true }] };

        Assert.Throws<InvoiceParseException>(() => extractor.Extract("<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///etc/passwd'>]><x>&e;</x>", definition));
        Assert.Throws<InvoiceParseException>(() => extractor.Extract(Xml, definition));
    }
}

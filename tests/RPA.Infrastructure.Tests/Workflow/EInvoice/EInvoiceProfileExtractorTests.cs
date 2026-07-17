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

    [Fact]
    public void Extract_FallbackRegex_UsedWhenPrimarySourceEmpty()
    {
        const string xml = """
            <Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"
                     xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2">
              <cbc:Note>Odeme IBAN: TR120001200012345678901234 uzerinden.</cbc:Note>
            </Invoice>
            """;
        var definition = new EInvoiceProfileDefinition
        {
            Fields =
            [
                new EInvoiceFieldDefinition
                {
                    Name = "iban",
                    Source = "XPath",
                    ValueXPath = "//cbc:PaymentID",
                    FallbackRegex = @"TR\d{24}",
                    Type = "string",
                },
            ],
        };

        var result = new EInvoiceProfileExtractor().Extract(xml, definition);

        Assert.Equal("TR120001200012345678901234", result["iban"]);
    }

    [Fact]
    public void Extract_FallbackRegex_NotUsedWhenPrimaryFindsValue()
    {
        const string xml = """
            <Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"
                     xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2">
              <cbc:ID>FTR2026001</cbc:ID>
            </Invoice>
            """;
        var definition = new EInvoiceProfileDefinition
        {
            Fields =
            [
                new EInvoiceFieldDefinition
                {
                    Name = "faturaNo",
                    Source = "XPath",
                    ValueXPath = "//cbc:ID",
                    FallbackRegex = @"YANLIS\d+",
                    Type = "string",
                },
            ],
        };

        var result = new EInvoiceProfileExtractor().Extract(xml, definition);

        Assert.Equal("FTR2026001", result["faturaNo"]);
    }

    [Fact]
    public void Extract_FallbackRegex_MultipleCollectsAllMatches()
    {
        const string xml = """
            <Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"
                     xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2">
              <cbc:Note>IBAN1: TR110001200012345678901234</cbc:Note>
              <cbc:Note>IBAN2: TR220001200012345678901234</cbc:Note>
            </Invoice>
            """;
        var definition = new EInvoiceProfileDefinition
        {
            Fields =
            [
                new EInvoiceFieldDefinition
                {
                    Name = "ibanlar",
                    Source = "XPath",
                    ValueXPath = "//cbc:PaymentID",
                    FallbackRegex = @"TR\d{24}",
                    Type = "string",
                    Multiple = true,
                },
            ],
        };

        var result = new EInvoiceProfileExtractor().Extract(xml, definition);

        var values = Assert.IsType<List<object>>(result["ibanlar"]);
        Assert.Equal(2, values.Count);
        Assert.Equal("TR110001200012345678901234", values[0]);
        Assert.Equal("TR220001200012345678901234", values[1]);
    }

    [Fact]
    public void Extract_FallbackRegex_WithNamedGroup()
    {
        const string xml = """
            <Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"
                     xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2">
              <cbc:Note>KUR: 32,45 TL</cbc:Note>
            </Invoice>
            """;
        var definition = new EInvoiceProfileDefinition
        {
            Fields =
            [
                new EInvoiceFieldDefinition
                {
                    Name = "kur",
                    Source = "XPath",
                    ValueXPath = "//cbc:ExchangeRate",
                    FallbackRegex = @"KUR[:= ]+(?<deger>\d+(?:[.,]\d+)?)",
                    FallbackGroup = "deger",
                    Type = "decimal",
                },
            ],
        };

        var result = new EInvoiceProfileExtractor().Extract(xml, definition);

        Assert.Equal(32.45m, result["kur"]);
    }

    [Fact]
    public void Extract_FallbackRegex_RequiredFieldStillMissing_Throws()
    {
        const string xml = """
            <Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"
                     xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2">
              <cbc:Note>iban bilgisi yok</cbc:Note>
            </Invoice>
            """;
        var definition = new EInvoiceProfileDefinition
        {
            Fields =
            [
                new EInvoiceFieldDefinition
                {
                    Name = "iban",
                    Source = "XPath",
                    ValueXPath = "//cbc:PaymentID",
                    FallbackRegex = @"TR\d{24}",
                    Type = "string",
                    Required = true,
                },
            ],
        };

        var exception = Assert.Throws<InvoiceParseException>(() => new EInvoiceProfileExtractor().Extract(xml, definition));
        Assert.Contains("iban", exception.Message);
    }

    [Theory]
    [InlineData("1.234,56", "1234.56")]
    [InlineData("1,234.56", "1234.56")]
    [InlineData("32,45", "32.45")]
    [InlineData("32.45", "32.45")]
    public void Extract_DecimalField_AcceptsTurkishAndEnglishFormats(string rawValue, string expected)
    {
        var xml = $"""
            <Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"
                     xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2">
              <cbc:Note>{rawValue}</cbc:Note>
            </Invoice>
            """;
        var definition = new EInvoiceProfileDefinition
        {
            Fields = [new EInvoiceFieldDefinition { Name = "tutar", Source = "XPath", ValueXPath = "//cbc:Note", Type = "decimal" }],
        };

        var result = new EInvoiceProfileExtractor().Extract(xml, definition);

        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), result["tutar"]);
    }

    [Theory]
    [InlineData("2026-07-16")]
    [InlineData("16.07.2026")]
    [InlineData("16/07/2026")]
    public void Extract_DateField_AcceptsTurkishFormats(string rawValue)
    {
        var xml = $"""
            <Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"
                     xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2">
              <cbc:Note>{rawValue}</cbc:Note>
            </Invoice>
            """;
        var definition = new EInvoiceProfileDefinition
        {
            Fields = [new EInvoiceFieldDefinition { Name = "tarih", Source = "XPath", ValueXPath = "//cbc:Note", Type = "date" }],
        };

        var result = new EInvoiceProfileExtractor().Extract(xml, definition);

        Assert.Equal(new DateOnly(2026, 7, 16), result["tarih"]);
    }
}

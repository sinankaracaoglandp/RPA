namespace RPA.Infrastructure.Tests.Workflow.EInvoice;

using RPA.Infrastructure.Workflow.Activities.EInvoice;
using Xunit;

public sealed class UblInvoiceParserTests
{
    [Fact]
    public void Parse_ReadsNamespacedHeaderPartiesTotalsAndLines()
    {
        var invoice = new UblInvoiceParser().Parse(SampleUbl.Xml);

        Assert.Equal("FTR202600001", invoice.InvoiceNumber);
        Assert.Equal("123e4567-e89b-12d3-a456-426614174000", invoice.Uuid);
        Assert.Equal(new DateOnly(2026, 7, 15), invoice.IssueDate);
        Assert.Equal("SATIS", invoice.InvoiceType);
        Assert.Equal("TEMELFATURA", invoice.ProfileId);
        Assert.Equal("TRY", invoice.Currency);
        Assert.Equal("Satıcı AŞ", invoice.Supplier!.Name);
        Assert.Equal("1234567890", invoice.Supplier.TaxId);
        Assert.Equal("Alıcı Ltd", invoice.Customer!.Name);
        Assert.Equal("0987654321", invoice.Customer.TaxId);
        Assert.Equal(100m, invoice.TaxExclusiveAmount);
        Assert.Equal(120m, invoice.TaxInclusiveAmount);
        Assert.Equal(120m, invoice.PayableAmount);
        var line = Assert.Single(invoice.Lines);
        Assert.Equal("1", line.Id);
        Assert.Equal("STK-1", line.ItemCode);
        Assert.Equal("Ürün A", line.Name);
        Assert.Equal(2m, line.Quantity);
        Assert.Equal("C62", line.UnitCode);
        Assert.Equal(50m, line.UnitPrice);
        Assert.Equal(100m, line.LineExtensionAmount);
    }

    [Fact]
    public void Parse_RejectsDtdAndExternalEntities()
    {
        const string xml = "<!DOCTYPE x [<!ENTITY ext SYSTEM 'file:///c:/windows/win.ini'>]><Invoice>&ext;</Invoice>";

        Assert.Throws<InvoiceParseException>(() => new UblInvoiceParser().Parse(xml));
    }

    [Fact]
    public void ParseFile_ReadsSmallXmlFile()
    {
        var filePath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(filePath, SampleUbl.Xml);

            var invoice = new UblInvoiceParser().ParseFile(filePath);

            Assert.Equal("FTR202600001", invoice.InvoiceNumber);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void ParseFile_RejectsFileExceedingCharacterLimit()
    {
        var filePath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(filePath, new string('x', 21));
            var parser = new UblInvoiceParser(new InvoiceParseOptions(MaxCharacters: 20));

            Assert.Throws<InvoiceParseException>(() => parser.ParseFile(filePath));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void Models_ExposeStableWorkflowShape()
    {
        var invoice = new InvoiceData
        {
            InvoiceNumber = "FTR202600001",
            Lines = [new InvoiceLineData { Name = "Kalem", Quantity = 2m }],
            CustomFields = new Dictionary<string, object?> { ["orderNumber"] = "S-42" }
        };

        Assert.Equal("FTR202600001", invoice.InvoiceNumber);
        Assert.Equal(2m, invoice.Lines.Single().Quantity);
        Assert.Equal("S-42", invoice.CustomFields["orderNumber"]);
    }

    [Fact]
    public void Parse_AppliesNamedRegexGroupToEveryNote()
    {
        var rules = new[] { new InvoiceMappingRule("orderNumber", "InvoiceNotes", null, null, @"Sipariş No:\s*(?<value>\S+)", "value") };

        var invoice = new UblInvoiceParser().Parse(SampleUbl.WithNotes("Sipariş No: S-42", "IBAN: TR12 0006 2000 1234 5678 9012 34", "1 USD = 32,4567 TL"), rules);

        Assert.Equal("S-42", invoice.CustomFields["orderNumber"]);
        Assert.Equal(32.4567m, invoice.ExchangeRate);
        Assert.Equal("TR120006200012345678901234", Assert.Single(invoice.PaymentAccounts));
    }

    [Fact]
    public void Parse_RequiredMappingWithoutMatchThrowsNamedError()
    {
        var rule = new InvoiceMappingRule("requiredCode", "XPath", null, "//cbc:Note", "YOK:(?<value>.+)", "value", Required: true);

        var exception = Assert.Throws<InvoiceParseException>(() => new UblInvoiceParser().Parse(SampleUbl.Xml, [rule]));

        Assert.Contains("requiredCode", exception.Message);
    }

    [Fact]
    public void Parse_AppliesScopedNamespaceAwareXPathAndConversions()
    {
        InvoiceMappingRule[] rules =
        [
            new("lineNames", "XPath", "//cac:InvoiceLine", "cac:Item/cbc:Name", null, null, Multiple: true),
            new("quantity", "XPath", null, "//cbc:InvoicedQuantity", null, null, Type: "decimal"),
            new("issued", "XPath", null, "//cbc:IssueDate", null, null, Type: "date"),
            new("approved", "InvoiceNotes", null, null, @"Approved:\s*(?<value>true)", "value", Type: "boolean")
        ];

        var invoice = new UblInvoiceParser().Parse(SampleUbl.WithNotes("Approved: true"), rules);

        Assert.Equal(new[] { "Ürün A" }, Assert.IsType<List<object?>>(invoice.CustomFields["lineNames"]));
        Assert.Equal(2m, invoice.CustomFields["quantity"]);
        Assert.Equal(new DateOnly(2026, 7, 15), invoice.CustomFields["issued"]);
        Assert.Equal(true, invoice.CustomFields["approved"]);
    }

    [Fact]
    public void Parse_PrefersStandardExchangeRateAndIbanOverNoteFallbacks()
    {
        var xml = SampleUbl.WithNotes("1 USD = 99,99 TL", "IBAN: TR120006200012345678901234")
            .Replace("<aggregate:LegalMonetaryTotal>", "<aggregate:PricingExchangeRate><basic:CalculationRate>31.25</basic:CalculationRate></aggregate:PricingExchangeRate><aggregate:PaymentMeans><aggregate:PayeeFinancialAccount><basic:ID>TR330006100519786457841326</basic:ID></aggregate:PayeeFinancialAccount></aggregate:PaymentMeans><aggregate:LegalMonetaryTotal>");

        var invoice = new UblInvoiceParser().Parse(xml);

        Assert.Equal(31.25m, invoice.ExchangeRate);
        Assert.Equal("TR330006100519786457841326", Assert.Single(invoice.PaymentAccounts));
    }

    [Fact]
    public void Parse_RegexTimeoutThrowsNamedParseError()
    {
        var parser = new UblInvoiceParser(new InvoiceParseOptions(RegexTimeout: TimeSpan.FromMilliseconds(1)));
        var rule = new InvoiceMappingRule("dangerous", "InvoiceNotes", null, null, "(a+)+$", null);
        var xml = SampleUbl.WithNotes(new string('a', 100_000) + "!");

        var exception = Assert.Throws<InvoiceParseException>(() => parser.Parse(xml, [rule]));

        Assert.Contains("dangerous", exception.Message);
    }

    [Fact]
    public void Parse_LineNotesCanReturnMultipleConvertedValues()
    {
        var xml = SampleUbl.Xml.Replace("<basic:ID>1</basic:ID>", "<basic:ID>1</basic:ID><basic:Note>10</basic:Note><basic:Note>20</basic:Note>");
        var rule = new InvoiceMappingRule("lineValues", "LineNotes", null, null, null, null, "integer", Multiple: true);

        var invoice = new UblInvoiceParser().Parse(xml, [rule]);

        Assert.Equal(new object?[] { 10L, 20L }, Assert.IsType<List<object?>>(invoice.CustomFields["lineValues"]));
    }

    private static class SampleUbl
    {
        public static string WithNotes(params string[] notes) => Xml.Replace(
            "<basic:UBLVersionID>",
            string.Concat(notes.Select(note => $"<basic:Note>{note}</basic:Note>")) + "<basic:UBLVersionID>");

        public const string Xml = """
            <Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"
                     xmlns:basic="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2"
                     xmlns:aggregate="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2">
              <basic:UBLVersionID>2.1</basic:UBLVersionID>
              <basic:CustomizationID>TR1.2</basic:CustomizationID>
              <basic:ProfileID>TEMELFATURA</basic:ProfileID>
              <basic:ID>FTR202600001</basic:ID>
              <basic:UUID>123e4567-e89b-12d3-a456-426614174000</basic:UUID>
              <basic:IssueDate>2026-07-15</basic:IssueDate>
              <basic:InvoiceTypeCode>SATIS</basic:InvoiceTypeCode>
              <basic:DocumentCurrencyCode>TRY</basic:DocumentCurrencyCode>
              <aggregate:AccountingSupplierParty><aggregate:Party>
                <aggregate:PartyIdentification><basic:ID schemeID="VKN">1234567890</basic:ID></aggregate:PartyIdentification>
                <aggregate:PartyName><basic:Name>Satıcı AŞ</basic:Name></aggregate:PartyName>
                <aggregate:PartyTaxScheme><aggregate:TaxScheme><basic:Name>Kadıköy</basic:Name></aggregate:TaxScheme></aggregate:PartyTaxScheme>
              </aggregate:Party></aggregate:AccountingSupplierParty>
              <aggregate:AccountingCustomerParty><aggregate:Party>
                <aggregate:PartyIdentification><basic:ID schemeID="VKN">0987654321</basic:ID></aggregate:PartyIdentification>
                <aggregate:PartyName><basic:Name>Alıcı Ltd</basic:Name></aggregate:PartyName>
              </aggregate:Party></aggregate:AccountingCustomerParty>
              <aggregate:LegalMonetaryTotal>
                <basic:TaxExclusiveAmount currencyID="TRY">100</basic:TaxExclusiveAmount>
                <basic:TaxInclusiveAmount currencyID="TRY">120</basic:TaxInclusiveAmount>
                <basic:PayableAmount currencyID="TRY">120</basic:PayableAmount>
              </aggregate:LegalMonetaryTotal>
              <aggregate:InvoiceLine>
                <basic:ID>1</basic:ID><basic:InvoicedQuantity unitCode="C62">2</basic:InvoicedQuantity>
                <basic:LineExtensionAmount currencyID="TRY">100</basic:LineExtensionAmount>
                <aggregate:Item><basic:Name>Ürün A</basic:Name><aggregate:SellersItemIdentification><basic:ID>STK-1</basic:ID></aggregate:SellersItemIdentification></aggregate:Item>
                <aggregate:Price><basic:PriceAmount currencyID="TRY">50</basic:PriceAmount></aggregate:Price>
              </aggregate:InvoiceLine>
            </Invoice>
            """;
    }
}

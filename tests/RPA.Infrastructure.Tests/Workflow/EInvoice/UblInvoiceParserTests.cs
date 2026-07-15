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

    private static class SampleUbl
    {
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

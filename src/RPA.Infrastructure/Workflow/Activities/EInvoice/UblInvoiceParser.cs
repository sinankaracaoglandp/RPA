namespace RPA.Infrastructure.Workflow.Activities.EInvoice;

using System.Globalization;
using System.Xml;
using System.Xml.Linq;

public sealed class UblInvoiceParser(InvoiceParseOptions? options = null)
{
    private const string BasicComponentsNamespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
    private const string AggregateComponentsNamespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    private readonly InvoiceParseOptions _options = options ?? new();

    public InvoiceData Parse(string xml, IReadOnlyList<InvoiceMappingRule>? mappings = null)
    {
        if (string.IsNullOrWhiteSpace(xml) || xml.Length > _options.MaxCharacters)
        {
            throw new InvoiceParseException("XML boş veya izin verilen boyutu aşıyor.");
        }

        try
        {
            using var stringReader = new StringReader(xml);
            using var xmlReader = XmlReader.Create(stringReader, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = _options.MaxCharacters
            });
            var document = XDocument.Load(xmlReader, LoadOptions.None);
            return ReadStandardFields(document, mappings ?? []);
        }
        catch (XmlException exception)
        {
            throw new InvoiceParseException($"Geçersiz veya güvensiz XML: {exception.Message}");
        }
    }

    public InvoiceData ParseFile(string filePath, IReadOnlyList<InvoiceMappingRule>? mappings = null) =>
        Parse(File.ReadAllText(filePath), mappings);

    private static InvoiceData ReadStandardFields(XDocument document, IReadOnlyList<InvoiceMappingRule> mappings)
    {
        _ = mappings;
        var root = document.Root ?? throw new InvoiceParseException("XML belge kökü içermiyor.");
        var basic = ResolveNamespace(root, BasicComponentsNamespace);
        var aggregate = ResolveNamespace(root, AggregateComponentsNamespace);
        var monetaryTotal = root.Element(aggregate + "LegalMonetaryTotal");

        return new InvoiceData
        {
            InvoiceNumber = Value(root.Element(basic + "ID")),
            Uuid = Value(root.Element(basic + "UUID")),
            IssueDate = ParseDate(Value(root.Element(basic + "IssueDate"))),
            InvoiceType = Value(root.Element(basic + "InvoiceTypeCode")),
            ProfileId = Value(root.Element(basic + "ProfileID")),
            Currency = Value(root.Element(basic + "DocumentCurrencyCode")),
            Supplier = ReadParty(root.Element(aggregate + "AccountingSupplierParty"), aggregate, basic),
            Customer = ReadParty(root.Element(aggregate + "AccountingCustomerParty"), aggregate, basic),
            TaxExclusiveAmount = ParseDecimal(Value(monetaryTotal?.Element(basic + "TaxExclusiveAmount"))),
            TaxInclusiveAmount = ParseDecimal(Value(monetaryTotal?.Element(basic + "TaxInclusiveAmount"))),
            PayableAmount = ParseDecimal(Value(monetaryTotal?.Element(basic + "PayableAmount"))),
            Lines = root.Elements(aggregate + "InvoiceLine").Select(line => ReadLine(line, aggregate, basic)).ToList()
        };
    }

    private static XNamespace ResolveNamespace(XElement root, string namespaceUri)
    {
        var declaration = root.Attributes().FirstOrDefault(attribute =>
            attribute.IsNamespaceDeclaration && attribute.Value == namespaceUri);
        return declaration?.Value ?? namespaceUri;
    }

    private static InvoicePartyData? ReadParty(XElement? partyContainer, XNamespace aggregate, XNamespace basic)
    {
        var party = partyContainer?.Element(aggregate + "Party");
        if (party is null)
        {
            return null;
        }

        return new InvoicePartyData
        {
            Name = Value(party.Element(aggregate + "PartyName")?.Element(basic + "Name"))
                ?? Value(party.Element(aggregate + "PartyLegalEntity")?.Element(basic + "RegistrationName")),
            TaxId = Value(party.Elements(aggregate + "PartyIdentification")
                .Select(identification => identification.Element(basic + "ID"))
                .FirstOrDefault(id => id is not null)),
            TaxOffice = Value(party.Element(aggregate + "PartyTaxScheme")
                ?.Element(aggregate + "TaxScheme")?.Element(basic + "Name"))
        };
    }

    private static InvoiceLineData ReadLine(XElement line, XNamespace aggregate, XNamespace basic)
    {
        var quantity = line.Element(basic + "InvoicedQuantity");
        var item = line.Element(aggregate + "Item");
        return new InvoiceLineData
        {
            Id = Value(line.Element(basic + "ID")),
            ItemCode = Value(item?.Element(aggregate + "SellersItemIdentification")?.Element(basic + "ID")),
            Name = Value(item?.Element(basic + "Name")),
            Quantity = ParseDecimal(Value(quantity)),
            UnitCode = quantity?.Attribute("unitCode")?.Value,
            UnitPrice = ParseDecimal(Value(line.Element(aggregate + "Price")?.Element(basic + "PriceAmount"))),
            LineExtensionAmount = ParseDecimal(Value(line.Element(basic + "LineExtensionAmount"))),
            Notes = line.Elements(basic + "Note").Select(note => note.Value).ToList()
        };
    }

    private static string? Value(XElement? element) =>
        string.IsNullOrWhiteSpace(element?.Value) ? null : element.Value.Trim();

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    private static decimal? ParseDecimal(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ? number : null;
}

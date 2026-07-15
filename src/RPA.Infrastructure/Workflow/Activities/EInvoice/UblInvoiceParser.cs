namespace RPA.Infrastructure.Workflow.Activities.EInvoice;

using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

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

    public InvoiceData ParseFile(string filePath, IReadOnlyList<InvoiceMappingRule>? mappings = null)
    {
        using var reader = new StreamReader(filePath);
        var buffer = new char[checked(_options.MaxCharacters + 1)];
        var charactersRead = reader.ReadBlock(buffer, 0, buffer.Length);
        if (charactersRead > _options.MaxCharacters)
        {
            throw new InvoiceParseException("XML boş veya izin verilen boyutu aşıyor.");
        }

        return Parse(new string(buffer, 0, charactersRead), mappings);
    }

    private InvoiceData ReadStandardFields(XDocument document, IReadOnlyList<InvoiceMappingRule> mappings)
    {
        var root = document.Root ?? throw new InvoiceParseException("XML belge kökü içermiyor.");
        XNamespace basic = BasicComponentsNamespace;
        XNamespace aggregate = AggregateComponentsNamespace;
        var monetaryTotal = root.Element(aggregate + "LegalMonetaryTotal");

        var notes = root.Elements(basic + "Note").Select(note => note.Value.Trim()).ToList();
        var invoice = new InvoiceData
        {
            InvoiceNumber = Value(root.Element(basic + "ID")),
            Uuid = Value(root.Element(basic + "UUID")),
            IssueDate = ParseDate(Value(root.Element(basic + "IssueDate"))),
            InvoiceType = Value(root.Element(basic + "InvoiceTypeCode")),
            ProfileId = Value(root.Element(basic + "ProfileID")),
            Currency = Value(root.Element(basic + "DocumentCurrencyCode")),
            Notes = notes,
            Supplier = ReadParty(root.Element(aggregate + "AccountingSupplierParty"), aggregate, basic),
            Customer = ReadParty(root.Element(aggregate + "AccountingCustomerParty"), aggregate, basic),
            TaxExclusiveAmount = ParseDecimal(Value(monetaryTotal?.Element(basic + "TaxExclusiveAmount"))),
            TaxInclusiveAmount = ParseDecimal(Value(monetaryTotal?.Element(basic + "TaxInclusiveAmount"))),
            PayableAmount = ParseDecimal(Value(monetaryTotal?.Element(basic + "PayableAmount"))),
            Lines = root.Elements(aggregate + "InvoiceLine").Select(line => ReadLine(line, aggregate, basic)).ToList()
        };

        invoice.ExchangeRate = ParseDecimal(Value(root.Element(aggregate + "PricingExchangeRate")?.Element(basic + "CalculationRate")));
        if (invoice.ExchangeRate is null)
        {
            var fallback = FindExchangeRate(notes);
            invoice.ExchangeRate = fallback.Value;
            if (fallback.Source is not null) invoice.ExtractionSources["exchangeRate"] = fallback.Source;
        }
        var accounts = root.Elements(aggregate + "PaymentMeans").SelectMany(means => means.Elements(aggregate + "PayeeFinancialAccount"))
            .Select(account => Value(account.Element(basic + "ID"))).Where(value => value is not null).Cast<string>().ToList();
        if (accounts.Count > 0) invoice.PaymentAccounts.AddRange(accounts);
        else
        {
            var fallback = FindIbans(notes);
            invoice.PaymentAccounts.AddRange(fallback.Values);
            if (fallback.Source is not null) invoice.ExtractionSources["paymentAccounts"] = fallback.Source;
        }
        foreach (var mapping in mappings)
        {
            var result = ApplyRule(document, mapping);
            if (result.Value is not null)
            {
                invoice.CustomFields[mapping.Name] = result.Value;
                if (result.SourceNote is not null) invoice.ExtractionSources[mapping.Name] = result.SourceNote;
            }
        }
        return invoice;
    }

    private (object? Value, string? SourceNote) ApplyRule(XDocument document, InvoiceMappingRule rule)
    {
        var ns = CreateNamespaceManager(document);
        IEnumerable<string> sourceValues = rule.Source switch
        {
            "InvoiceNotes" => document.Root?.Elements(XName.Get("Note", BasicComponentsNamespace)).Select(note => note.Value) ?? [],
            "LineNotes" => document.Descendants(XName.Get("InvoiceLine", AggregateComponentsNamespace)).SelectMany(line => line.Elements(XName.Get("Note", BasicComponentsNamespace))).Select(note => note.Value),
            "XPath" => ReadXPathValues(document, rule, ns),
            _ => []
        };
        var values = sourceValues.Select(source => (Source: source, Match: Match(source, rule))).Where(item => item.Match is not null)
            .Select(item => (item.Source, Value: ConvertValue(item.Match!, rule))).ToList();
        if (values.Count == 0 && rule.Required) throw new InvoiceParseException($"Zorunlu eşleme bulunamadı: {rule.Name}");
        var sourceNote = rule.Source is "InvoiceNotes" or "LineNotes" ? values.FirstOrDefault().Source : null;
        return (rule.Multiple ? values.Select(item => item.Value).ToList() : values.FirstOrDefault().Value, sourceNote);
    }

    private static IEnumerable<string> ReadXPathValues(XDocument document, InvoiceMappingRule rule, XmlNamespaceManager ns)
    {
        var scopes = string.IsNullOrWhiteSpace(rule.ScopeXPath) ? [document.CreateNavigator()!] : document.XPathSelectElements(rule.ScopeXPath, ns).Select(element => element.CreateNavigator()).ToArray();
        foreach (var scope in scopes)
        {
            var iterator = scope.Select(string.IsNullOrWhiteSpace(rule.ValueXPath) ? "." : rule.ValueXPath, ns);
            while (iterator.MoveNext()) if (!string.IsNullOrWhiteSpace(iterator.Current?.Value)) yield return iterator.Current.Value.Trim();
        }
    }

    private string? Match(string value, InvoiceMappingRule rule)
    {
        if (string.IsNullOrWhiteSpace(rule.Regex)) return value;
        try
        {
            var match = Regex.Match(value, rule.Regex, RegexOptions.CultureInvariant, _options.EffectiveRegexTimeout);
            if (!match.Success) return null;
            var group = string.IsNullOrWhiteSpace(rule.Group) ? match.Groups[0] : match.Groups[rule.Group];
            return group.Success ? group.Value : null;
        }
        catch (RegexMatchTimeoutException) { throw new InvoiceParseException($"Eşleme regex zaman aşımı: {rule.Name}"); }
    }

    private static object ConvertValue(string value, InvoiceMappingRule rule) => rule.Type.ToLowerInvariant() switch
    {
        "string" => value,
        "decimal" => ParseDecimal(value) ?? throw ConversionError(rule.Name),
        "integer" => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer) ? integer : throw ConversionError(rule.Name),
        "date" => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : throw ConversionError(rule.Name),
        "boolean" => bool.TryParse(value, out var boolean) ? boolean : value switch { "1" => true, "0" => false, _ => throw ConversionError(rule.Name) },
        _ => throw new InvoiceParseException($"Desteklenmeyen eşleme tipi ({rule.Type}): {rule.Name}")
    };

    private static InvoiceParseException ConversionError(string name) => new($"Eşleme tür dönüşümü başarısız: {name}");

    private static XmlNamespaceManager CreateNamespaceManager(XDocument document)
    {
        var manager = new XmlNamespaceManager(document.CreateNavigator()!.NameTable);
        manager.AddNamespace("cbc", BasicComponentsNamespace);
        manager.AddNamespace("cac", AggregateComponentsNamespace);
        manager.AddNamespace("inv", document.Root?.Name.NamespaceName ?? string.Empty);
        return manager;
    }

    private static (decimal? Value, string? Source) FindExchangeRate(IEnumerable<string> notes)
    {
        var regex = new Regex(@"\b(?:1\s+)?[A-Z]{3}\s*=\s*(?<value>\d+(?:[.,]\d+)?)\s*(?:TL|TRY)\b", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(500));
        foreach (var note in notes)
        {
            var match = regex.Match(note);
            if (match.Success) return (ParseDecimal(match.Groups["value"].Value), note);
        }
        return (null, null);
    }

    private static (List<string> Values, string? Source) FindIbans(IEnumerable<string> notes)
    {
        var regex = new Regex(@"\bTR(?:\s*\d){24}\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(500));
        var values = new List<string>();
        string? source = null;
        foreach (var note in notes)
        {
            foreach (Match match in regex.Matches(note))
            {
                source ??= note;
                values.Add(string.Concat(match.Value.Where(character => !char.IsWhiteSpace(character))).ToUpperInvariant());
            }
        }
        return (values.Distinct(StringComparer.Ordinal).ToList(), source);
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

    private static decimal? ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var lastSeparator = Math.Max(value.LastIndexOf('.'), value.LastIndexOf(','));
        var normalized = string.Concat(value.Select((character, index) => character switch
        {
            '.' or ',' when index == lastSeparator => '.',
            '.' or ',' => '\0',
            _ => character
        }).Where(character => character != '\0'));
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ? number : null;
    }
}

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
            ValidateDepth(xml);
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

    private void ValidateDepth(string xml)
    {
        using var input = new StringReader(xml);
        using var reader = XmlReader.Create(input, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = _options.MaxCharacters
        });
        while (reader.Read())
            if (reader.Depth > _options.MaxDepth)
                throw new InvoiceParseException("XML izin verilen derinlik sınırını aşıyor.");
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
        var taxes = ReadTaxes(root.Element(aggregate + "TaxTotal"), aggregate, basic, false);
        var withholdingTaxes = ReadTaxes(root.Element(aggregate + "WithholdingTaxTotal"), aggregate, basic, true);

        var notes = root.Elements(basic + "Note").Select(note => note.Value.Trim()).ToList();
        var invoice = new InvoiceData
        {
            InvoiceNumber = Value(root.Element(basic + "ID")),
            Uuid = Value(root.Element(basic + "UUID")),
            IssueDate = ParseDate(Value(root.Element(basic + "IssueDate"))),
            IssueTime = ParseTime(Value(root.Element(basic + "IssueTime"))),
            InvoiceType = Value(root.Element(basic + "InvoiceTypeCode")),
            ProfileId = Value(root.Element(basic + "ProfileID")),
            Currency = Value(root.Element(basic + "DocumentCurrencyCode")),
            Notes = notes,
            Supplier = ReadParty(root.Element(aggregate + "AccountingSupplierParty"), aggregate, basic),
            Customer = ReadParty(root.Element(aggregate + "AccountingCustomerParty"), aggregate, basic),
            TaxExclusiveAmount = ParseDecimal(Value(monetaryTotal?.Element(basic + "TaxExclusiveAmount"))),
            TaxInclusiveAmount = ParseDecimal(Value(monetaryTotal?.Element(basic + "TaxInclusiveAmount"))),
            TaxAmount = ParseDecimal(Value(root.Element(aggregate + "TaxTotal")?.Element(basic + "TaxAmount"))),
            AllowanceTotalAmount = ParseDecimal(Value(monetaryTotal?.Element(basic + "AllowanceTotalAmount"))),
            PayableAmount = ParseDecimal(Value(monetaryTotal?.Element(basic + "PayableAmount"))),
            Taxes = taxes,
            WithholdingTaxes = withholdingTaxes,
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
            (object? Value, string? SourceNote) result;
            try { result = ApplyRule(document, invoice, mapping); }
            catch (InvoiceParseException) { throw; }
            catch (XPathException) { throw new InvoiceParseException($"Geçersiz XPath eşlemesi: {mapping.Name}"); }
            catch (ArgumentException) { throw new InvoiceParseException($"Geçersiz regex eşlemesi: {mapping.Name}"); }
            if (result.Value is not null)
            {
                invoice.CustomFields[mapping.Name] = result.Value;
                if (result.SourceNote is not null) invoice.ExtractionSources[mapping.Name] = result.SourceNote;
            }
        }
        return invoice;
    }

    private (object? Value, string? SourceNote) ApplyRule(XDocument document, InvoiceData invoice, InvoiceMappingRule rule)
    {
        var ns = CreateNamespaceManager(document);
        IEnumerable<string> sourceValues = rule.Source switch
        {
            "Standard" => ReadStandardValues(invoice, rule.ValueXPath ?? rule.Name),
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

    private static IEnumerable<string> ReadStandardValues(InvoiceData invoice, string field)
    {
        object? value = field.Trim().ToLowerInvariant() switch
        {
            "uuid" => invoice.Uuid,
            "id" or "invoicenumber" or "invoice.number" => invoice.InvoiceNumber,
            "issuedate" or "invoice.date" => invoice.IssueDate,
            "issuetime" or "invoice.time" => invoice.IssueTime,
            "invoicetype" or "type" => invoice.InvoiceType,
            "profileid" or "scenario" => invoice.ProfileId,
            "currency" or "documentcurrencycode" => invoice.Currency,
            "suppliername" or "supplier.name" => invoice.Supplier?.Name,
            "suppliertaxid" or "supplier.taxid" => invoice.Supplier?.TaxId,
            "customername" or "customer.name" => invoice.Customer?.Name,
            "customertaxid" or "customer.taxid" => invoice.Customer?.TaxId,
            "taxexclusiveamount" => invoice.TaxExclusiveAmount,
            "taxamount" => invoice.TaxAmount,
            "allowancetotalamount" or "discounttotal" => invoice.AllowanceTotalAmount,
            "taxinclusiveamount" => invoice.TaxInclusiveAmount,
            "payableamount" => invoice.PayableAmount,
            "exchangerate" => invoice.ExchangeRate,
            "paymentaccounts" or "ibans" => invoice.PaymentAccounts,
            _ => null
        };

        if (value is IEnumerable<string> values) return values;
        var formatted = value switch
        {
            null => null,
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TimeOnly time => time.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
        return formatted is null ? [] : [formatted];
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
            var regex = new Regex(rule.Regex, RegexOptions.CultureInvariant, _options.EffectiveRegexTimeout);
            if (!string.IsNullOrWhiteSpace(rule.Group) &&
                !regex.GetGroupNames().Contains(rule.Group, StringComparer.Ordinal))
                throw new InvoiceParseException($"Geçersiz regex grubu: {rule.Name}");
            var match = regex.Match(value);
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
                ?.Element(aggregate + "TaxScheme")?.Element(basic + "Name")),
            Address = ReadAddress(party.Element(aggregate + "PostalAddress"), aggregate, basic),
            Contact = ReadContact(party.Element(aggregate + "Contact"), basic)
        };
    }

    private static InvoiceAddressData? ReadAddress(XElement? address, XNamespace aggregate, XNamespace basic) => address is null ? null : new()
    {
        StreetName = Value(address.Element(basic + "StreetName")),
        CitySubdivisionName = Value(address.Element(basic + "CitySubdivisionName")),
        CityName = Value(address.Element(basic + "CityName")),
        PostalZone = Value(address.Element(basic + "PostalZone")),
        CountryName = Value(address.Element(aggregate + "Country")?.Element(basic + "Name"))
    };

    private static InvoiceContactData? ReadContact(XElement? contact, XNamespace basic) => contact is null ? null : new()
    {
        Name = Value(contact.Element(basic + "Name")),
        Telephone = Value(contact.Element(basic + "Telephone")),
        Email = Value(contact.Element(basic + "ElectronicMail"))
    };

    private static List<InvoiceTaxData> ReadTaxes(XElement? total, XNamespace aggregate, XNamespace basic, bool withholding) =>
        total?.Elements(aggregate + "TaxSubtotal").Select(subtotal =>
        {
            var category = subtotal.Element(aggregate + "TaxCategory");
            var scheme = category?.Element(aggregate + "TaxScheme");
            return new InvoiceTaxData(
                Value(scheme?.Element(basic + "TaxTypeCode")),
                Value(scheme?.Element(basic + "Name")),
                ParseDecimal(Value(subtotal.Element(basic + "Percent"))),
                ParseDecimal(Value(subtotal.Element(basic + "TaxAmount"))),
                Value(category?.Element(basic + "TaxExemptionReasonCode")),
                Value(category?.Element(basic + "TaxExemptionReason")),
                withholding);
        }).ToList() ?? [];

    private static InvoiceLineData ReadLine(XElement line, XNamespace aggregate, XNamespace basic)
    {
        var quantity = line.Element(basic + "InvoicedQuantity");
        var item = line.Element(aggregate + "Item");
        return new InvoiceLineData
        {
            Id = Value(line.Element(basic + "ID")),
            ItemCode = Value(item?.Element(aggregate + "SellersItemIdentification")?.Element(basic + "ID")),
            Name = Value(item?.Element(basic + "Name")),
            Description = Value(item?.Element(basic + "Description")),
            Quantity = ParseDecimal(Value(quantity)),
            UnitCode = quantity?.Attribute("unitCode")?.Value,
            UnitPrice = ParseDecimal(Value(line.Element(aggregate + "Price")?.Element(basic + "PriceAmount"))),
            LineExtensionAmount = ParseDecimal(Value(line.Element(basic + "LineExtensionAmount"))),
            DiscountAmount = line.Elements(aggregate + "AllowanceCharge")
                .Where(charge => string.Equals(Value(charge.Element(basic + "ChargeIndicator")), "false", StringComparison.OrdinalIgnoreCase))
                .Select(charge => ParseDecimal(Value(charge.Element(basic + "Amount"))) ?? 0m).Sum(),
            Taxes = ReadTaxes(line.Element(aggregate + "TaxTotal"), aggregate, basic, false),
            WithholdingTaxes = ReadTaxes(line.Element(aggregate + "WithholdingTaxTotal"), aggregate, basic, true),
            Notes = line.Elements(basic + "Note").Select(note => note.Value).ToList()
        };
    }

    private static string? Value(XElement? element) =>
        string.IsNullOrWhiteSpace(element?.Value) ? null : element.Value.Trim();

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    private static TimeOnly? ParseTime(string? value) =>
        TimeOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : null;

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

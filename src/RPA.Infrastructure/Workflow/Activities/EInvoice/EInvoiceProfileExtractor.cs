namespace RPA.Infrastructure.Workflow.Activities.EInvoice;

using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using RPA.Application.EInvoiceProfiles;

public sealed class EInvoiceProfileExtractor(InvoiceParseOptions? options = null)
{
    private const string Basic = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
    private const string Aggregate = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    private readonly InvoiceParseOptions _options = options ?? new();

    public Dictionary<string, object?> Extract(string xml, EInvoiceProfileDefinition definition)
    {
        var document = Load(xml);
        var namespaces = Namespaces(document);
        var result = ReadFields(document.CreateNavigator()!, document, definition.Fields, namespaces);
        foreach (var collection in definition.Collections)
        {
            try
            {
                result[collection.Name] = document.XPathSelectElements(collection.ScopeXPath, namespaces)
                    .Select(element => ReadFields(element.CreateNavigator(), document, collection.Fields, namespaces)).ToList();
            }
            catch (XPathException) { throw new InvoiceParseException($"Geçersiz koleksiyon XPath'i: {collection.Name}"); }
        }
        return result;
    }

    private Dictionary<string, object?> ReadFields(XPathNavigator scope, XDocument document, IEnumerable<EInvoiceFieldDefinition> fields, XmlNamespaceManager namespaces)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields)
        {
            var raw = Values(scope, document, field, namespaces).Select(value => ApplyRegex(value, field))
                .Where(value => value is not null).Select(value => value!).ToList();
            if (raw.Count == 0 && !string.IsNullOrWhiteSpace(field.FallbackRegex))
                raw = ApplyFallbackRegex(ScopeText(scope), field);
            var values = raw.Select(value => Convert(value, field)).ToList();
            if (values.Count == 0 && field.Required) throw new InvoiceParseException($"Zorunlu profil alanı bulunamadı: {field.Name}");
            result[field.Name] = field.Multiple ? values : values.FirstOrDefault();
        }
        return result;
    }

    private static string ScopeText(XPathNavigator scope) =>
        string.Join("\n", scope.SelectDescendants(XPathNodeType.Text, false).Cast<XPathNavigator>()
            .Select(navigator => navigator.Value.Trim()).Where(value => value.Length > 0));

    private List<string> ApplyFallbackRegex(string text, EInvoiceFieldDefinition field)
    {
        try
        {
            var regex = new Regex(field.FallbackRegex!, RegexOptions.CultureInvariant, _options.EffectiveRegexTimeout);
            if (!string.IsNullOrWhiteSpace(field.FallbackGroup) && !regex.GetGroupNames().Contains(field.FallbackGroup, StringComparer.Ordinal))
                throw new InvoiceParseException($"Geçersiz fallback regex grubu: {field.Name}");
            var matches = regex.Matches(text)
                .Select(match => string.IsNullOrWhiteSpace(field.FallbackGroup) ? match.Value : match.Groups[field.FallbackGroup].Value)
                .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim());
            return (field.Multiple ? matches : matches.Take(1)).ToList();
        }
        catch (RegexMatchTimeoutException) { throw new InvoiceParseException($"Profil fallback regex zaman aşımı: {field.Name}"); }
        catch (ArgumentException) { throw new InvoiceParseException($"Geçersiz profil fallback regex'i: {field.Name}"); }
    }

    private static IEnumerable<string> Values(XPathNavigator scope, XDocument document, EInvoiceFieldDefinition field, XmlNamespaceManager namespaces)
    {
        try
        {
            return field.Source switch
            {
                "XPath" => Select(scope, field.ValueXPath ?? ".", namespaces),
                "InvoiceNotes" => document.Root?.Elements(XName.Get("Note", Basic)).Select(x => x.Value) ?? [],
                "LineNotes" => scope.SelectDescendants("Note", Basic, true).Cast<XPathNavigator>().Select(x => x.Value),
                "Standard" => Select(scope, StandardXPath(field.ValueXPath ?? field.Name), namespaces),
                _ => []
            };
        }
        catch (XPathException) { throw new InvoiceParseException($"Geçersiz alan XPath'i: {field.Name}"); }
    }

    private static IEnumerable<string> Select(XPathNavigator scope, string xpath, XmlNamespaceManager namespaces)
    {
        var iterator = scope.Select(xpath, namespaces);
        var values = new List<string>();
        while (iterator.MoveNext()) if (!string.IsNullOrWhiteSpace(iterator.Current?.Value)) values.Add(iterator.Current.Value.Trim());
        return values;
    }

    private string? ApplyRegex(string value, EInvoiceFieldDefinition field)
    {
        if (string.IsNullOrWhiteSpace(field.Regex)) return value.Trim();
        try
        {
            var regex = new Regex(field.Regex, RegexOptions.CultureInvariant, _options.EffectiveRegexTimeout);
            if (!string.IsNullOrWhiteSpace(field.Group) && !regex.GetGroupNames().Contains(field.Group, StringComparer.Ordinal))
                throw new InvoiceParseException($"Geçersiz regex grubu: {field.Name}");
            var match = regex.Match(value);
            if (!match.Success) return null;
            return string.IsNullOrWhiteSpace(field.Group) ? match.Value : match.Groups[field.Group].Value;
        }
        catch (RegexMatchTimeoutException) { throw new InvoiceParseException($"Profil regex zaman aşımı: {field.Name}"); }
        catch (ArgumentException) { throw new InvoiceParseException($"Geçersiz profil regex'i: {field.Name}"); }
    }

    private static object Convert(string value, EInvoiceFieldDefinition field) => field.Type.ToLowerInvariant() switch
    {
        "string" => value,
        "integer" => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer) ? integer : throw Conversion(field.Name),
        "decimal" => decimal.TryParse(value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ? number : throw Conversion(field.Name),
        "date" => DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : throw Conversion(field.Name),
        "boolean" => bool.TryParse(value, out var boolean) ? boolean : value switch { "1" => true, "0" => false, _ => throw Conversion(field.Name) },
        _ => throw Conversion(field.Name)
    };

    private XDocument Load(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml) || xml.Length > _options.MaxCharacters) throw new InvoiceParseException("XML boş veya izin verilen boyutu aşıyor.");
        try
        {
            using var input = new StringReader(xml);
            using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = _options.MaxCharacters });
            while (reader.Read()) if (reader.Depth > _options.MaxDepth) throw new InvoiceParseException("XML izin verilen derinlik sınırını aşıyor.");
            using var input2 = new StringReader(xml);
            using var reader2 = XmlReader.Create(input2, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = _options.MaxCharacters });
            return XDocument.Load(reader2, LoadOptions.None);
        }
        catch (XmlException exception) { throw new InvoiceParseException($"Geçersiz veya güvensiz XML: {exception.Message}"); }
    }

    private static XmlNamespaceManager Namespaces(XDocument document)
    {
        var manager = new XmlNamespaceManager(document.CreateNavigator()!.NameTable);
        manager.AddNamespace("cbc", Basic); manager.AddNamespace("cac", Aggregate);
        manager.AddNamespace("inv", document.Root?.Name.NamespaceName ?? string.Empty);
        return manager;
    }

    private static string StandardXPath(string field) => field.Trim().ToLowerInvariant() switch
    {
        "id" or "invoicenumber" or "invoice.number" => "//cbc:ID[1]",
        "issuedate" or "invoice.date" => "//cbc:IssueDate[1]",
        "currency" => "//cbc:DocumentCurrencyCode[1]",
        "itemcode" or "line.itemcode" => "cac:Item/cac:SellersItemIdentification/cbc:ID",
        "name" or "line.name" => "cac:Item/cbc:Name",
        "quantity" or "line.quantity" => "cbc:InvoicedQuantity",
        "unitprice" or "line.unitprice" => "cac:Price/cbc:PriceAmount",
        _ => field
    };

    private static InvoiceParseException Conversion(string name) => new($"Profil alan tür dönüşümü başarısız: {name}");
}

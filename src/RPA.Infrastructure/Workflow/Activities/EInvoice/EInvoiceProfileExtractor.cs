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
        var documentPrefixes = DocumentPrefixes(document);
        var result = ReadFields(document.CreateNavigator()!, document, definition.Fields, namespaces, documentPrefixes);
        foreach (var collection in definition.Collections)
        {
            try
            {
                result[collection.Name] = document
                    .XPathSelectElements(NamespaceSafeXPath(collection.ScopeXPath, documentPrefixes), namespaces)
                    .Select(element => ReadFields(element.CreateNavigator(), document, collection.Fields, namespaces, documentPrefixes)).ToList();
            }
            catch (XPathException) { throw new InvoiceParseException($"Geçersiz koleksiyon XPath'i: {collection.Name}"); }
        }
        return result;
    }

    /// <summary>Belgenin kökünde bildirilen namespace önekleri (varsayılan ns için "" anahtarı).</summary>
    private static Dictionary<string, string> DocumentPrefixes(XDocument document)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var attribute in document.Root?.Attributes().Where(a => a.IsNamespaceDeclaration) ?? [])
        {
            var prefix = attribute.Name.Namespace == XNamespace.Xmlns ? attribute.Name.LocalName : string.Empty;
            result[prefix] = attribute.Value;
        }
        return result;
    }

    /// <summary>
    /// XPath segmentlerini namespace'e toleranslı biçime çevirir — Studio eşleme editörünün
    /// tasarım-anı önizlemesiyle (einvoice-mapping.model.ts <c>namespaceSafeXPath</c>) BİREBİR
    /// aynı semantik. Editör XPath'leri belgenin etiket adlarından üretir ve UBL kökü varsayılan
    /// namespace'te olduğundan segment öneksiz olur (<c>/Invoice/cbc:ID</c>); ham hâliyle .NET
    /// bunu "namespace'siz Invoice" diye arayıp hiçbir şey bulamıyordu (tüm alanlar null).
    /// Bilinmeyen önekler olduğu gibi bırakılır; onları <see cref="Namespaces"/> yöneticisi çözer.
    /// </summary>
    private static string NamespaceSafeXPath(string? xpath, IReadOnlyDictionary<string, string> documentPrefixes)
    {
        if (string.IsNullOrWhiteSpace(xpath)) { return xpath ?? string.Empty; }

        return string.Join("/", xpath.Split('/').Select(segment =>
        {
            if (segment.Length == 0 || segment is "." or ".."
                || segment.StartsWith('@') || segment.Contains('(', StringComparison.Ordinal))
            {
                return segment;
            }

            var colon = segment.IndexOf(':', StringComparison.Ordinal);
            if (colon >= 0)
            {
                var prefix = segment[..colon];
                var rest = segment[(colon + 1)..];
                var bracket = rest.IndexOf('[', StringComparison.Ordinal);
                var name = bracket >= 0 ? rest[..bracket] : rest;
                var predicate = bracket >= 0 ? rest[bracket..] : string.Empty;
                // Eksen belirteci (descendant-or-self::x) veya bilinmeyen önek → dokunma.
                return name.Length > 0 && documentPrefixes.TryGetValue(prefix, out var uri)
                    ? $"*[local-name()='{name}' and namespace-uri()='{uri}']{predicate}"
                    : segment;
            }

            var bracketIndex = segment.IndexOf('[', StringComparison.Ordinal);
            var localName = bracketIndex >= 0 ? segment[..bracketIndex] : segment;
            var tail = bracketIndex >= 0 ? segment[bracketIndex..] : string.Empty;
            return $"*[local-name()='{localName}']{tail}";
        }));
    }

    private Dictionary<string, object?> ReadFields(XPathNavigator scope, XDocument document, IEnumerable<EInvoiceFieldDefinition> fields, XmlNamespaceManager namespaces, IReadOnlyDictionary<string, string> documentPrefixes)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields)
        {
            var raw = Values(scope, document, field, namespaces, documentPrefixes).Select(value => ApplyRegex(value, field))
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

    private static IEnumerable<string> Values(XPathNavigator scope, XDocument document, EInvoiceFieldDefinition field, XmlNamespaceManager namespaces, IReadOnlyDictionary<string, string> documentPrefixes)
    {
        try
        {
            return field.Source switch
            {
                "XPath" => Select(scope, NamespaceSafeXPath(field.ValueXPath ?? ".", documentPrefixes), namespaces),
                "InvoiceNotes" => document.Root?.Elements(XName.Get("Note", Basic)).Select(x => x.Value) ?? [],
                "LineNotes" => scope.SelectDescendants("Note", Basic, true).Cast<XPathNavigator>().Select(x => x.Value),
                "Standard" => Select(scope, NamespaceSafeXPath(StandardXPath(field.ValueXPath ?? field.Name), documentPrefixes), namespaces),
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
        "decimal" => ParseDecimal(value, field.Name),
        "date" => ParseDate(value, field.Name),
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

    private static decimal ParseDecimal(string value, string name)
    {
        var normalized = value.Trim();
        if (normalized.Contains(',') && normalized.Contains('.'))
        {
            // Son gelen ayraç ondalık ayracıdır: "1.234,56" → TR, "1,234.56" → EN.
            normalized = normalized.LastIndexOf(',') > normalized.LastIndexOf('.')
                ? normalized.Replace(".", string.Empty).Replace(',', '.')
                : normalized.Replace(",", string.Empty);
        }
        else
        {
            normalized = normalized.Replace(',', '.');
        }
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
            ? number : throw Conversion(name);
    }

    private static readonly string[] DateFormats = ["yyyy-MM-dd", "dd.MM.yyyy", "dd/MM/yyyy"];

    private static DateOnly ParseDate(string value, string name) =>
        DateOnly.TryParseExact(value.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date : throw Conversion(name);

    private static InvoiceParseException Conversion(string name) => new($"Profil alan tür dönüşümü başarısız: {name}");
}

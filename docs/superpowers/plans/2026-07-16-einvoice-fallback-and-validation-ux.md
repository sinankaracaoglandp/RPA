# E-Fatura Profili: XPath→Regex Fallback + Görsel Doğrulama UX İmplementasyon Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** E-fatura profil alanlarına "önce XPath dene, bulamazsan regex ile ham metinde ara" fallback zinciri eklemek; adresleme editörüne alan bazlı görsel doğrulama, regex sihirbazı ve koleksiyon-XPath göreceliliği düzeltmesi getirmek; designer'da profil sürüm uyarısı göstermek.

**Architecture:** Backend'de `EInvoiceFieldDefinition`'a `FallbackRegex`/`FallbackGroup` alanları eklenir (kontrat değişikliği); `EInvoiceProfileExtractor` birincil kaynak boş dönerse scope metni üzerinde fallback regex çalıştırır. Studio tarafında aynı model `einvoice-mapping.model.ts`'de yansıtılır, önizleme "hangi kural buldu" bilgisini taşır, ham JSON önizleme yerine alan-satırı + koleksiyon-tablosu görünümü gelir ve yeni bir `regex-wizard` bileşeni regex bilmeyen kullanıcıya desen üretir.

**Tech Stack:** .NET 10 (xUnit, `dotnet test`), Angular standalone components (Jasmine/Karma, `npm test`), System.Text.Json, System.Xml.XPath.

## Global Constraints

- TDD zorunlu: her görevde failing test → minimal impl → pass → commit (proje CLAUDE.md).
- Onion katman yönü korunur: Application, Infrastructure'a bağımlı olamaz.
- Kontrat değişikliği önce `CLAUDE.md`'ye `## Kontrat Değişikliği — [tarih]` kaydı olarak yazılır (Task 2, Step 1).
- Hata mesajları XML içeriğini veya eşleşen hassas değeri içermez (mevcut güvenlik kuralı).
- Regex çalıştırmaları timeout korumalıdır: backend `InvoiceParseOptions.EffectiveRegexTimeout`, Studio draft önizlemesi worker + 75 ms.
- Alan/koleksiyon adları `^[A-Za-z_][A-Za-z0-9_]*$` identifier kuralına uyar (mevcut validator).
- Commit mesajları Conventional Commits + `Co-Authored-By: Claude Opus <noreply@anthropic.com>` footer'ı.
- Studio testleri: `cd src/RPA.Studio` içinde `npm test -- --watch=false` (Karma tek sefer koşar). Backend: repo kökünde `dotnet test tests/<Layer>.Tests`.
- **Dikkat:** Çalışma ağacında bu plana ait olmayan staged migration dosyaları var (`20260716125041_AddEInvoiceProfiles*`). Onlara dokunma; her commit'te yalnız kendi dosyalarını `git add` ile ekle.

---

### Task 1: `[Authorize]` geri alınması

`EInvoiceProfilesController` üzerinde lokal test için auth yorum satırına alınmış. Bu haliyle asla commit edilmemeli.

**Files:**
- Modify: `src/RPA.WebAPI/Controllers/EInvoiceProfilesController.cs` (satır ~10)

**Interfaces:**
- Consumes: yok.
- Produces: yok (davranış eski haline döner).

- [ ] **Step 1: Yorum satırını geri al**

Dosyada şu satırı bul:

```csharp
//[Authorize]
```

ve şununla değiştir:

```csharp
[Authorize]
```

- [ ] **Step 2: Mevcut controller testlerinin geçtiğini doğrula**

Run: `dotnet test tests/RPA.WebAPI.Tests --filter "FullyQualifiedName~EInvoiceProfiles"`
Expected: PASS (testler auth'u test host üzerinden zaten ele alıyor; kırmızı test görürsen testin auth beklentisini incele, `[Authorize]`'ı tekrar kaldırma).

- [ ] **Step 3: Commit**

```bash
git add src/RPA.WebAPI/Controllers/EInvoiceProfilesController.cs
git commit -m "fix(webapi): einvoice profiles controller auth geri alindi

Lokal test icin kapatilan [Authorize] attribute'u geri eklendi.

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 2: Backend — Fallback regex kontratı (definition + validator + extractor)

Bugün regex, XPath'in bulduğu değerin **üzerine** uygulanan bir filtre ([EInvoiceProfileExtractor.cs](../../src/RPA.Infrastructure/Workflow/Activities/EInvoice/EInvoiceProfileExtractor.cs) `ReadFields` → `ApplyRegex`). XPath hiçbir şey bulamazsa regex hiç çalışmaz. Bu görev, alan tanımına **fallback** regex ekler: birincil kaynak (XPath/Standard/Notes + mevcut regex filtresi) boş dönerse, scope'un düz metni üzerinde fallback regex koşulur.

**Files:**
- Modify: `src/RPA.Application/EInvoiceProfiles/EInvoiceProfileDefinition.cs`
- Modify: `src/RPA.Application/EInvoiceProfiles/EInvoiceProfileDefinitionValidator.cs`
- Modify: `src/RPA.Infrastructure/Workflow/Activities/EInvoice/EInvoiceProfileExtractor.cs`
- Modify: `CLAUDE.md` (kontrat kaydı)
- Test: `tests/RPA.Application.Tests/EInvoiceProfiles/EInvoiceProfileDefinitionValidatorTests.cs`
- Test: `tests/RPA.Infrastructure.Tests/Workflow/EInvoice/EInvoiceProfileExtractorTests.cs`

**Interfaces:**
- Consumes: `EInvoiceProfileDefinition`, `EInvoiceFieldDefinition`, `InvoiceParseException`, `InvoiceParseOptions.EffectiveRegexTimeout` (hepsi mevcut).
- Produces: `EInvoiceFieldDefinition.FallbackRegex : string?`, `EInvoiceFieldDefinition.FallbackGroup : string?` — JSON'da `fallbackRegex`/`fallbackGroup` (case-insensitive deserialize mevcut). Task 4 (Studio modeli) bu isimlere birebir uyar. Davranış sözleşmesi: birincil değerler boşsa VE `FallbackRegex` doluysa fallback koşulur; `Multiple=true` tüm eşleşmeler, `false` ilk eşleşme; grup adı geçersizse/desen geçersizse/timeout olursa `InvoiceParseException`.

- [ ] **Step 1: Kontrat kaydını CLAUDE.md'ye ekle**

`CLAUDE.md` dosyasının sonuna (Kontrat Değişiklik Prosedürü uyarınca, implementasyondan **önce**) şu bölümü ekle:

```markdown
## Kontrat Değişikliği — 2026-07-16 (E-Fatura profil alanı fallback regex)

`EInvoiceFieldDefinition`'a iki opsiyonel alan eklendi: `FallbackRegex` (string?) ve
`FallbackGroup` (string?). Anlamı: alanın birincil kaynağı (XPath/Standard/Notes + mevcut
`Regex` filtresi) hiçbir değer üretmezse, extractor scope'un düz metni (text node'lar
"\n" ile birleştirilmiş) üzerinde `FallbackRegex`'i koşar; `Multiple=true` tüm eşleşmeleri,
`false` ilk eşleşmeyi alır. Mevcut `Regex` alanının anlamı DEĞİŞMEDİ (XPath sonucu üzerine
filtre). Validator: `fallbackGroup` verilmişse `fallbackRegex` zorunlu; desen derlenemezse
BusinessException. Timeout `InvoiceParseOptions.EffectiveRegexTimeout` ile aynıdır.

Etkilenen paketler: EInvoice profil editörü (Studio `einvoice-mapping.model.ts` +
`einvoice-mapping-editor`), `EInvoiceProfileDefinitionValidator`, `EInvoiceProfileExtractor`.
`OutputSchemaJson` üretimi etkilenmez (fallback yalnız değer bulma stratejisidir, tip aynı).
Gerekçe: "önce XPath ile ara, bulamazsan regex ile ham metinde ara" kullanıcı akışı mevcut
modelde ifade edilemiyordu.
```

- [ ] **Step 2: Failing validator testlerini yaz**

`tests/RPA.Application.Tests/EInvoiceProfiles/EInvoiceProfileDefinitionValidatorTests.cs` dosyasına (mevcut test sınıfının içine) ekle:

```csharp
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
```

- [ ] **Step 3: Testlerin FAIL ettiğini gör**

Run: `dotnet test tests/RPA.Application.Tests --filter "FullyQualifiedName~EInvoiceProfileDefinitionValidatorTests"`
Expected: yeni 3 test FAIL (`FallbackRegex` property yok → derleme hatası). Derleme hatası da "failing test" sayılır.

- [ ] **Step 4: Definition modelini genişlet**

`src/RPA.Application/EInvoiceProfiles/EInvoiceProfileDefinition.cs` içinde `EInvoiceFieldDefinition` sınıfına iki property ekle (`Group`'tan sonra):

```csharp
public string? FallbackRegex { get; init; }
public string? FallbackGroup { get; init; }
```

- [ ] **Step 5: Validator'a fallback kurallarını ekle**

`EInvoiceProfileDefinitionValidator.ValidateField` metodunun sonuna ekle:

```csharp
if (string.IsNullOrWhiteSpace(field.FallbackRegex) && !string.IsNullOrWhiteSpace(field.FallbackGroup))
    throw new BusinessException($"fallbackGroup için fallbackRegex zorunludur: {field.Name}");
if (!string.IsNullOrWhiteSpace(field.FallbackRegex))
{
    try { _ = new Regex(field.FallbackRegex, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)); }
    catch (ArgumentException exception)
    { throw new BusinessException($"Geçersiz fallback regex deseni: {field.Name}", exception); }
}
```

(`using System.Text.RegularExpressions;` dosyada zaten var.)

- [ ] **Step 6: Validator testlerinin PASS ettiğini gör**

Run: `dotnet test tests/RPA.Application.Tests --filter "FullyQualifiedName~EInvoiceProfileDefinitionValidatorTests"`
Expected: PASS (yeni 3 dahil tümü).

- [ ] **Step 7: Failing extractor testlerini yaz**

`tests/RPA.Infrastructure.Tests/Workflow/EInvoice/EInvoiceProfileExtractorTests.cs` dosyasına (mevcut test sınıfının içine, mevcut testlerin örnek-XML kurulum desenini izleyerek) ekle:

```csharp
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
                ValueXPath = "//cbc:PaymentID",   // örnekte yok → birincil kaynak boş döner
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
```

Not: Task 4'te `decimal` dönüşümü TR formatına toleranslı hale gelmeden önce `"32,45"` mevcut `Convert` ile de geçer (`Replace(',', '.')` var) — bu test şimdiden yeşil olabilir; fallback yolunu sınadığı için yine de gerekli.

- [ ] **Step 8: Extractor testlerinin FAIL ettiğini gör**

Run: `dotnet test tests/RPA.Infrastructure.Tests --filter "FullyQualifiedName~EInvoiceProfileExtractorTests"`
Expected: yeni testler FAIL (fallback yolu olmadığından `iban` null döner / required test yanlış mesajla geçebilir — ilk iki testin FAIL olduğunu mutlaka gör).

- [ ] **Step 9: Extractor'a fallback yolunu ekle**

`src/RPA.Infrastructure/Workflow/Activities/EInvoice/EInvoiceProfileExtractor.cs` içinde `ReadFields` metodunu şununla değiştir:

```csharp
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
```

Dikkat: koleksiyon alanlarında `scope` satır elementinin navigator'ıdır → fallback yalnız o satırın metninde arar (istenen davranış).

- [ ] **Step 10: Extractor testlerinin PASS ettiğini gör**

Run: `dotnet test tests/RPA.Infrastructure.Tests --filter "FullyQualifiedName~EInvoiceProfileExtractorTests"`
Expected: PASS (yeni 5 dahil tümü).

- [ ] **Step 11: İlgili tüm backend testlerini koş**

Run: `dotnet test tests/RPA.Application.Tests` sonra `dotnet test tests/RPA.Infrastructure.Tests --filter "FullyQualifiedName~EInvoice"`
Expected: PASS.

- [ ] **Step 12: Commit**

```bash
git add CLAUDE.md src/RPA.Application/EInvoiceProfiles/EInvoiceProfileDefinition.cs src/RPA.Application/EInvoiceProfiles/EInvoiceProfileDefinitionValidator.cs src/RPA.Infrastructure/Workflow/Activities/EInvoice/EInvoiceProfileExtractor.cs tests/RPA.Application.Tests/EInvoiceProfiles/EInvoiceProfileDefinitionValidatorTests.cs tests/RPA.Infrastructure.Tests/Workflow/EInvoice/EInvoiceProfileExtractorTests.cs
git commit -m "feat(contract): e-fatura profil alanina fallback regex eklendi

EInvoiceFieldDefinition.FallbackRegex/FallbackGroup: birincil kaynak
(XPath/Standard/Notes) bos donerse scope duz metninde regex ile arama.
Multiple=true tum eslesmeler, false ilk eslesme. Validator: group icin
regex zorunlu + desen derlenebilirlik kontrolu.

Kontrat Degisikligi (CLAUDE.md dosyasinda belirtildi).

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 3: Backend — TR tarih/tutar dönüşüm toleransı

`Convert` bugün invariant culture kullanıyor: `"1.234,56"` ve `"16.07.2026"` patlar. Regex/fallback ile fatura notlarından çekilen değerler tipik olarak TR formatındadır.

**Files:**
- Modify: `src/RPA.Infrastructure/Workflow/Activities/EInvoice/EInvoiceProfileExtractor.cs` (`Convert` metodu)
- Test: `tests/RPA.Infrastructure.Tests/Workflow/EInvoice/EInvoiceProfileExtractorTests.cs`

**Interfaces:**
- Consumes: Task 2'nin `ReadFields` yapısı (Convert çağrısı aynı kalır).
- Produces: `decimal` alanlar `"1.234,56"` → `1234.56m`, `"1,234.56"` → `1234.56m`, `"32,45"` → `32.45m`; `date` alanlar `"yyyy-MM-dd"`, `"dd.MM.yyyy"`, `"dd/MM/yyyy"` kabul eder ve `DateOnly` döner. Task 4 (Studio önizleme) bu davranışı birebir aynalar.

- [ ] **Step 1: Failing testleri yaz**

`EInvoiceProfileExtractorTests.cs`'e ekle:

```csharp
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
```

- [ ] **Step 2: FAIL gör**

Run: `dotnet test tests/RPA.Infrastructure.Tests --filter "FullyQualifiedName~EInvoiceProfileExtractorTests"`
Expected: `1.234,56`, `1,234.56`, `16.07.2026`, `16/07/2026` durumları FAIL (`InvoiceParseException: Profil alan tür dönüşümü başarısız`).

- [ ] **Step 3: Convert'i toleranslı hale getir**

`EInvoiceProfileExtractor.cs` içinde `Convert` metodundaki `"decimal"` ve `"date"` kollarını değiştir ve iki yardımcı ekle:

```csharp
"decimal" => ParseDecimal(value, field.Name),
"date" => ParseDate(value, field.Name),
```

```csharp
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
```

- [ ] **Step 4: PASS gör**

Run: `dotnet test tests/RPA.Infrastructure.Tests --filter "FullyQualifiedName~EInvoice"`
Expected: PASS (tüm mevcut EInvoice testleri dahil — `yyyy-MM-dd` yolu `TryParseExact` listesinin başında olduğundan geriye uyumlu).

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Infrastructure/Workflow/Activities/EInvoice/EInvoiceProfileExtractor.cs tests/RPA.Infrastructure.Tests/Workflow/EInvoice/EInvoiceProfileExtractorTests.cs
git commit -m "feat(infrastructure): e-fatura profil donusumunde TR tarih/tutar toleransi

decimal: 1.234,56 / 1,234.56 / 32,45 kabul edilir (son ayrac ondaliktir).
date: yyyy-MM-dd, dd.MM.yyyy, dd/MM/yyyy formatlari.

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 4: Studio modeli — fallback + TR dönüşüm paritesi + matchedBy

Studio önizleme motoru ([einvoice-mapping.model.ts](../../src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping.model.ts)) backend ile aynı semantiği taşımalı: fallback regex, TR format toleransı ve önizlemeye "hangi kural buldu" (`matchedBy`) bilgisi.

**Files:**
- Modify: `src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping.model.ts`
- Test (create): `src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping.model.spec.ts`

**Interfaces:**
- Consumes: mevcut `EInvoiceMappingRule`, `previewRule`, `previewProfileDefinition`, `convert` (dosya-içi).
- Produces:
  - `EInvoiceMappingRule.fallbackRegex?: string | null`, `EInvoiceMappingRule.fallbackGroup?: string | null` (JSON adları backend `fallbackRegex`/`fallbackGroup` ile birebir).
  - `RulePreview.matchedBy?: 'xpath' | 'fallback'`.
  - `export function relativizeXPath(valueXPath: string, scopeXPath: string): string` (Task 5 kullanır).
  Task 6 (önizleme paneli) ve Task 7 (sihirbaz) bu tipleri tüketir.

- [ ] **Step 1: Failing model testlerini yaz**

Yeni dosya `src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping.model.spec.ts`:

```typescript
import {
  EInvoiceMappingRule,
  parseSampleXml,
  previewRule,
  relativizeXPath,
} from './einvoice-mapping.model';

const SAMPLE = `<Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"
  xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2">
  <cbc:ID>FTR2026001</cbc:ID>
  <cbc:Note>Odeme IBAN: TR120001200012345678901234</cbc:Note>
  <cbc:Note>Toplam 1.234,56 TL, tarih 16.07.2026</cbc:Note>
</Invoice>`;

function rule(overrides: Partial<EInvoiceMappingRule>): EInvoiceMappingRule {
  return { name: 'alan', source: 'XPath', valueXPath: '', type: 'string', required: false, multiple: false, ...overrides };
}

describe('einvoice-mapping.model fallback ve dönüşüm', () => {
  let document: XMLDocument;
  beforeEach(() => { document = parseSampleXml(SAMPLE).document; });

  it('XPath bulursa matchedBy=xpath döner ve fallback çalışmaz', () => {
    const preview = previewRule(rule({ valueXPath: '/Invoice/cbc:ID', fallbackRegex: 'YANLIS\\d+' }), document);
    expect(preview.converted).toBe('FTR2026001');
    expect(preview.matchedBy).toBe('xpath');
  });

  it('XPath boş dönerse fallback regex ham metinde arar', () => {
    const preview = previewRule(rule({ valueXPath: '/Invoice/cbc:PaymentID', fallbackRegex: 'TR\\d{24}' }), document);
    expect(preview.converted).toBe('TR120001200012345678901234');
    expect(preview.matchedBy).toBe('fallback');
  });

  it('fallback named group ile değer seçer', () => {
    const preview = previewRule(
      rule({ valueXPath: '/Invoice/cbc:Kur', fallbackRegex: 'Toplam (?<deger>\\d{1,3}(?:\\.\\d{3})*,\\d+)', fallbackGroup: 'deger', type: 'decimal' }),
      document,
    );
    expect(preview.converted).toBe(1234.56);
  });

  it('decimal TR binlik/ondalık formatını çevirir', () => {
    const preview = previewRule(rule({ valueXPath: '/Invoice/cbc:X', fallbackRegex: '1\\.234,56', type: 'decimal' }), document);
    expect(preview.converted).toBe(1234.56);
  });

  it('date dd.MM.yyyy formatını ISO değere çevirir', () => {
    const preview = previewRule(rule({ valueXPath: '/Invoice/cbc:X', fallbackRegex: '\\d{2}\\.\\d{2}\\.\\d{4}', type: 'date' }), document);
    expect(preview.converted).toBe('2026-07-16');
  });

  it('ikisi de bulamazsa required alan hata verir', () => {
    const preview = previewRule(rule({ valueXPath: '/Invoice/cbc:Yok', fallbackRegex: 'ASLA\\d+', required: true }), document);
    expect(preview.converted).toBeNull();
    expect(preview.error).toBe('Zorunlu değer bulunamadı.');
  });
});

describe('relativizeXPath', () => {
  it('mutlak scope önekini ./ ile değiştirir', () => {
    expect(relativizeXPath('/Invoice/cac:InvoiceLine/cbc:ID', '/Invoice/cac:InvoiceLine')).toBe('./cbc:ID');
  });
  it('// scope için son segmentten sonrasını göreceler', () => {
    expect(relativizeXPath('/Invoice/cac:InvoiceLine/cac:Item/cbc:Name', '//cac:InvoiceLine')).toBe('./cac:Item/cbc:Name');
  });
  it('zaten göreceli yolu değiştirmez', () => {
    expect(relativizeXPath('./cbc:ID', '//cac:InvoiceLine')).toBe('./cbc:ID');
  });
  it('scope ile eşleşmeyen yolu olduğu gibi bırakır', () => {
    expect(relativizeXPath('/Invoice/cbc:ID', '//cac:InvoiceLine')).toBe('/Invoice/cbc:ID');
  });
});
```

- [ ] **Step 2: FAIL gör**

Run: `cd src/RPA.Studio` sonra `npm test -- --watch=false`
Expected: yeni spec derleme hatasıyla FAIL (`fallbackRegex`, `matchedBy`, `relativizeXPath` yok).

- [ ] **Step 3: Modeli genişlet**

`einvoice-mapping.model.ts` değişiklikleri:

(a) Arayüzler:

```typescript
export interface EInvoiceMappingRule {
  name: string;
  source: 'Standard' | 'XPath' | 'InvoiceNotes' | 'LineNotes';
  scopeXPath?: string | null;
  valueXPath?: string | null;
  regex?: string | null;
  group?: string | null;
  fallbackRegex?: string | null;
  fallbackGroup?: string | null;
  type: 'string' | 'decimal' | 'integer' | 'date' | 'boolean';
  required: boolean;
  multiple: boolean;
}

export interface RulePreview {
  raw: string | string[] | null;
  converted: unknown;
  matchedBy?: 'xpath' | 'fallback';
  error?: string;
}
```

(b) Yardımcılar (dosya sonuna, `kurPreset`'ten önce):

```typescript
function documentText(root: Node): string {
  const parts: string[] = [];
  const visit = (node: Node): void => {
    if (node.nodeType === Node.TEXT_NODE) {
      const text = node.textContent?.trim();
      if (text) parts.push(text);
    }
    node.childNodes.forEach(visit);
  };
  visit(root);
  return parts.join('\n');
}

function fallbackValues(rule: EInvoiceMappingRule, text: string): string[] {
  const expression = new RegExp(rule.fallbackRegex!, 'g');
  const values: string[] = [];
  for (const match of text.matchAll(expression)) {
    const selected = !rule.fallbackGroup
      ? match[0]
      : /^\d+$/.test(rule.fallbackGroup) ? match[Number(rule.fallbackGroup)] : match.groups?.[rule.fallbackGroup];
    if (selected === undefined) throw new Error(`Fallback regex group '${rule.fallbackGroup}' bulunamadı.`);
    if (selected.trim()) values.push(selected.trim());
    if (!rule.multiple && values.length) break;
  }
  return values;
}

export function relativizeXPath(valueXPath: string, scopeXPath: string): string {
  const value = valueXPath.trim();
  const scope = scopeXPath.trim();
  if (!value || !scope || value.startsWith('.')) return value;
  if (scope.startsWith('//')) {
    const last = scope.split('/').filter(Boolean).pop();
    if (!last) return value;
    const marker = `/${last}/`;
    const index = value.indexOf(marker);
    return index >= 0 ? `./${value.slice(index + marker.length)}` : value;
  }
  return value.startsWith(`${scope}/`) ? `./${value.slice(scope.length + 1)}` : value;
}
```

(c) `convert` fonksiyonunda `decimal` ve `date` kolları (backend Task 3 ile birebir aynı semantik):

```typescript
function convert(value: string, type: EInvoiceMappingRule['type']): unknown {
  if (type === 'string') return value;
  if (type === 'decimal') {
    let normalized = value.trim();
    if (normalized.includes(',') && normalized.includes('.')) {
      normalized = normalized.lastIndexOf(',') > normalized.lastIndexOf('.')
        ? normalized.split('.').join('').replace(',', '.')
        : normalized.split(',').join('');
    } else {
      normalized = normalized.replace(',', '.');
    }
    const parsed = Number(normalized);
    if (!Number.isFinite(parsed)) throw new Error('decimal dönüşümü başarısız.');
    return parsed;
  }
  if (type === 'integer') { if (!/^[+-]?\d+$/.test(value)) throw new Error('integer dönüşümü başarısız.'); return Number.parseInt(value, 10); }
  if (type === 'boolean') { if (/^(true|1)$/i.test(value)) return true; if (/^(false|0)$/i.test(value)) return false; throw new Error('boolean dönüşümü başarısız.'); }
  return convertDate(value);
}

function convertDate(value: string): string {
  const iso = /^(?<year>\d{4})-(?<month>\d{2})-(?<day>\d{2})$/.exec(value.trim());
  const tr = /^(?<day>\d{2})[./](?<month>\d{2})[./](?<year>\d{4})$/.exec(value.trim());
  const match = iso ?? tr;
  if (!match) throw new Error('date dönüşümü başarısız.');
  const year = Number(match.groups!['year']);
  const month = Number(match.groups!['month']);
  const day = Number(match.groups!['day']);
  const leap = year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
  const days = [31, leap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
  if (year === 0 || month < 1 || month > 12 || day < 1 || day > days[month - 1]) throw new Error('date dönüşümü başarısız.');
  return `${match.groups!['year'].padStart(4, '0')}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
}
```

Not: mevcut `convert` date kolu ISO string döndürüyordu; `convertDate` her iki formatı da ISO'ya normalize eder — mevcut testler kırılmaz.

(d) `previewRule` fallback + matchedBy (fonksiyonun tamamını değiştir):

```typescript
export function previewRule(rule: EInvoiceMappingRule, document: XMLDocument): RulePreview {
  try {
    let values = valuesFor(rule, document);
    if (rule.regex) {
      const expression = new RegExp(rule.regex);
      values = values.flatMap(value => {
        const match = expression.exec(value);
        if (!match) return [];
        if (!rule.group) return [match[0] ?? ''];
        const selected = /^\d+$/.test(rule.group) ? match[Number(rule.group)] : match.groups?.[rule.group];
        if (selected === undefined) throw new Error(`Regex group '${rule.group}' bulunamadı.`);
        return [selected];
      });
    }
    let matchedBy: RulePreview['matchedBy'] = values.length ? 'xpath' : undefined;
    if (!values.length && rule.fallbackRegex) {
      values = fallbackValues(rule, documentText(document.documentElement));
      if (values.length) matchedBy = 'fallback';
    }
    if (!values.length) return { raw: null, converted: rule.multiple ? [] : null, error: rule.required ? 'Zorunlu değer bulunamadı.' : undefined };
    const converted = values.map(value => convert(value, rule.type));
    return { raw: rule.multiple ? values : values[0], converted: rule.multiple ? converted : converted[0], matchedBy };
  } catch (error) {
    return { raw: null, converted: null, error: error instanceof Error ? error.message : String(error) };
  }
}
```

(e) `previewRuleInScope` içinde de aynı fallback bloğunu ekle — regex bloğundan sonra, `if (!values.length) return ...` satırından önce:

```typescript
    let matchedBy: RulePreview['matchedBy'] = values.length ? 'xpath' : undefined;
    if (!values.length && rule.fallbackRegex) {
      values = fallbackValues(rule, documentText(scope));
      if (values.length) matchedBy = 'fallback';
    }
```

ve dönüş satırını `{ raw: ..., converted: ..., matchedBy }` olacak şekilde güncelle.

- [ ] **Step 4: PASS gör**

Run: `cd src/RPA.Studio` sonra `npm test -- --watch=false`
Expected: yeni model spec'i ve mevcut `einvoice-mapping-editor.component.spec.ts` PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping.model.ts src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping.model.spec.ts
git commit -m "feat(studio): profil onizlemesine fallback regex + TR format paritesi

fallbackRegex/fallbackGroup alanlari, matchedBy (xpath|fallback) bilgisi,
decimal/date TR toleransi backend ile birebir; relativizeXPath yardimcisi.

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 5: Studio editörü — koleksiyon alanında göreceli XPath düzeltmesi (bug fix)

Ağaç tıklaması her zaman **mutlak** XPath üretir (`buildXPath`). Mutlak yol bir koleksiyon alanına eklenirse runtime her satır için ilk satırın değerini okur (yanlış). Alan koleksiyona eklenirken yol, koleksiyonun `scopeXPath`'ine göre göreceli hale getirilmeli.

**Files:**
- Modify: `src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.ts` (`addCollectionField` metodu)
- Test: `src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.spec.ts`

**Interfaces:**
- Consumes: Task 4'ün `relativizeXPath(valueXPath, scopeXPath)` fonksiyonu.
- Produces: davranış değişikliği — `addCollectionField` kaydetmeden önce `valueXPath`'i göreceler. Definition JSON çıktı formatı değişmez.

- [ ] **Step 1: Failing component testini yaz**

`einvoice-mapping-editor.component.spec.ts`'e (mevcut describe bloğu içine, dosyadaki component oluşturma desenini izleyerek) ekle:

```typescript
it('koleksiyona eklenen alanın mutlak XPath yolu scope\'a göre göreceli kaydedilir', () => {
  component.addCollection('satirlar', '/Invoice/cac:InvoiceLine');
  component.addCollectionField('satirlar', {
    name: 'MalzemeKodu',
    source: 'XPath',
    valueXPath: '/Invoice/cac:InvoiceLine/cac:Item/cbc:SellersItemIdentification/cbc:ID',
    type: 'string',
    required: false,
    multiple: false,
  });
  expect(component.collections[0].fields[0].valueXPath)
    .toBe('./cac:Item/cbc:SellersItemIdentification/cbc:ID');
});

it('koleksiyona eklenen zaten göreceli yol değişmez', () => {
  component.addCollection('satirlar', '//cac:InvoiceLine');
  component.addCollectionField('satirlar', {
    name: 'Miktar', source: 'XPath', valueXPath: './cbc:InvoicedQuantity',
    type: 'decimal', required: false, multiple: false,
  });
  expect(component.collections[0].fields[0].valueXPath).toBe('./cbc:InvoicedQuantity');
});
```

- [ ] **Step 2: FAIL gör**

Run: `cd src/RPA.Studio` sonra `npm test -- --watch=false`
Expected: ilk test FAIL (mutlak yol olduğu gibi kaydediliyor).

- [ ] **Step 3: `addCollectionField`'i düzelt**

`einvoice-mapping-editor.component.ts` import satırına `relativizeXPath` ekle ve metodu şöyle değiştir:

```typescript
addCollectionField(collectionName: string, field: EInvoiceMappingRule): void {
  this.collections = this.collections.map(collection => {
    if (collection.name !== collectionName || !this.isIdentifier(field.name)) return collection;
    if (collection.fields.some(item => item.name.toLowerCase() === field.name.toLowerCase())) return collection;
    const valueXPath = field.valueXPath ? relativizeXPath(field.valueXPath, collection.scopeXPath) : field.valueXPath;
    return { ...collection, fields: [...collection.fields, { ...field, valueXPath }] };
  });
  this.emitProfileDefinition();
}
```

- [ ] **Step 4: PASS gör**

Run: `cd src/RPA.Studio` sonra `npm test -- --watch=false`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.ts src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.spec.ts
git commit -m "fix(studio): koleksiyon alaninda mutlak XPath scope'a gore gorecelenir

Agac tiklamasi mutlak yol uretiyordu; satir alanina eklenince runtime her
satirda ilk satirin degerini okuyordu.

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 6: Studio editörü — fallback alanları + alan bazlı görsel doğrulama paneli

Kullanıcının akışı: "alanı tanımla → bulunan değeri ekranda gör → doğrula". Ham JSON `<pre>` yerine: kural satırlarında bulunan değer + yeşil/kırmızı rozet + "XPath / Regex fallback" etiketi; koleksiyonlar için ilk 5 satırlık tablo. Kural formuna fallback regex/grup girişleri eklenir. JSON, `<details>` içinde teknik detay olarak kalır.

**Files:**
- Modify: `src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.ts`
- Modify: `src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.html`
- Modify: `src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.scss`
- Test: `src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.spec.ts`

**Interfaces:**
- Consumes: Task 4'ün `previewRule`, `previewProfileDefinition`, `RulePreview.matchedBy`, `EInvoiceMappingRule.fallbackRegex/fallbackGroup`.
- Produces: component public API — `savedRulePreviews(): Array<{ rule: EInvoiceMappingRule; preview: RulePreview }>`, `collectionPreviewRows(collection: EInvoiceCollectionDefinition): Array<Record<string, unknown>>`, `collectionColumns(collection: EInvoiceCollectionDefinition): string[]`, `previewText(preview: RulePreview): string`. Task 7 sihirbaz entegrasyonu bu component'in `draft` alanına yazar.

Not — worker kararı: draft önizlemesi mevcut worker+75 ms yolunda kalır (kullanıcı yazarken kötü desene karşı koruma). Kaydedilmiş kurallar zaten eklenirken doğrulandığından `savedRulePreviews` senkron `previewRule` kullanır; `previewProfileDefinition` de bugün senkron çalışıyor (mevcut davranışla tutarlı).

- [ ] **Step 1: Failing component testlerini yaz**

`einvoice-mapping-editor.component.spec.ts`'e ekle (spec dosyasındaki mevcut örnek XML yükleme desenini kullan; dosyada hazır bir örnek UBL sabiti varsa onu kullan, yoksa aşağıdaki sabiti dosya başına ekle):

```typescript
const PREVIEW_SAMPLE = `<Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"
  xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2"
  xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2">
  <cbc:ID>FTR2026001</cbc:ID>
  <cbc:Note>IBAN: TR120001200012345678901234</cbc:Note>
  <cac:InvoiceLine><cbc:ID>1</cbc:ID><cbc:InvoicedQuantity>2</cbc:InvoicedQuantity></cac:InvoiceLine>
  <cac:InvoiceLine><cbc:ID>2</cbc:ID><cbc:InvoicedQuantity>5</cbc:InvoicedQuantity></cac:InvoiceLine>
</Invoice>`;

it('kaydedilmiş kural önizlemesi bulunan değeri ve eşleşme kaynağını döner', () => {
  component.loadSampleXml(PREVIEW_SAMPLE);
  component.addRule({ name: 'faturaNo', source: 'XPath', valueXPath: '/Invoice/cbc:ID', type: 'string', required: false, multiple: false });
  component.addRule({ name: 'iban', source: 'XPath', valueXPath: '/Invoice/cbc:Yok', fallbackRegex: 'TR\\d{24}', type: 'string', required: false, multiple: false });

  const previews = component.savedRulePreviews();

  expect(previews[0].preview.converted).toBe('FTR2026001');
  expect(previews[0].preview.matchedBy).toBe('xpath');
  expect(previews[1].preview.converted).toBe('TR120001200012345678901234');
  expect(previews[1].preview.matchedBy).toBe('fallback');
});

it('koleksiyon önizlemesi ilk satırları tablo verisi olarak döner', () => {
  component.loadSampleXml(PREVIEW_SAMPLE);
  component.addCollection('satirlar', '//cac:InvoiceLine');
  component.addCollectionField('satirlar', { name: 'Miktar', source: 'XPath', valueXPath: './cbc:InvoicedQuantity', type: 'integer', required: false, multiple: false });

  const rows = component.collectionPreviewRows(component.collections[0]);

  expect(rows.length).toBe(2);
  expect(rows[0]['Miktar']).toBe(2);
  expect(component.collectionColumns(component.collections[0])).toEqual(['Miktar']);
});

it('örnek XML yokken kural önizlemesi açıklayıcı hata taşır', () => {
  component.addRule({ name: 'faturaNo', source: 'XPath', valueXPath: '/Invoice/cbc:ID', type: 'string', required: false, multiple: false });
  expect(component.savedRulePreviews()[0].preview.error).toBe('Örnek XML yüklenmedi.');
});
```

- [ ] **Step 2: FAIL gör**

Run: `cd src/RPA.Studio` sonra `npm test -- --watch=false`
Expected: `savedRulePreviews`/`collectionPreviewRows`/`collectionColumns` yok → derleme FAIL.

- [ ] **Step 3: Component metodlarını ekle**

`einvoice-mapping-editor.component.ts`'e (import'lara `previewRule` zaten var; `RulePreview` de var) şu metodları ekle:

```typescript
savedRulePreviews(): Array<{ rule: EInvoiceMappingRule; preview: RulePreview }> {
  if (!this.sampleDocument) {
    return this.rules.map(rule => ({ rule, preview: { raw: null, converted: null, error: 'Örnek XML yüklenmedi.' } }));
  }
  return this.rules.map(rule => ({ rule, preview: previewRule(rule, this.sampleDocument!) }));
}

collectionPreviewRows(collection: EInvoiceCollectionDefinition): Array<Record<string, unknown>> {
  if (!this.sampleDocument) return [];
  const preview = previewProfileDefinition({ fields: [], collections: [collection] }, this.sampleDocument);
  const rows = preview[collection.name];
  return Array.isArray(rows) ? rows.slice(0, 5) : [];
}

collectionColumns(collection: EInvoiceCollectionDefinition): string[] {
  return collection.fields.map(field => field.name);
}

previewText(preview: RulePreview): string {
  if (preview.error) return preview.error;
  if (preview.converted === null || preview.converted === undefined) return '—';
  return Array.isArray(preview.converted) ? preview.converted.map(String).join(', ') : String(preview.converted);
}
```

- [ ] **Step 4: Metod testlerinin PASS ettiğini gör**

Run: `cd src/RPA.Studio` sonra `npm test -- --watch=false`
Expected: PASS.

- [ ] **Step 5: Kural formuna fallback girişlerini, önizleme paneline görsel görünümü ekle**

`einvoice-mapping-editor.component.html`:

(a) Kural panelinde `Group` input'undan sonra ekle:

```html
    <label for="einvoice-rule-fallback-regex">Bulunamazsa regex ile ara (fallback)</label>
    <input id="einvoice-rule-fallback-regex" data-testid="einvoice-fallback-regex"
      [value]="draft.fallbackRegex ?? ''" (input)="updateDraft('fallbackRegex', $any($event.target).value)"
      placeholder="örn. TR\d{24}" />
    <label for="einvoice-rule-fallback-group">Fallback grup adı</label>
    <input id="einvoice-rule-fallback-group" data-testid="einvoice-fallback-group"
      [value]="draft.fallbackGroup ?? ''" (input)="updateDraft('fallbackGroup', $any($event.target).value)" />
```

(b) Önizleme panelinin tamamını (en alttaki `einvoice-preview-panel` div'i) şununla değiştir:

```html
  <div class="einvoice-mapping__panel" data-testid="einvoice-preview-panel">
    <h4>Doğrulama</h4>
    @if (!tree.length) {
      <p class="einvoice-mapping__empty">Bulunan değerleri görmek için örnek XML yükleyin.</p>
    }
    <ul class="einvoice-mapping__validation" aria-label="Alan doğrulama sonuçları">
      @for (item of savedRulePreviews(); track item.rule.name) {
        <li class="einvoice-mapping__validation-row"
          [class.einvoice-mapping__validation-row--ok]="!item.preview.error && item.preview.converted !== null"
          [class.einvoice-mapping__validation-row--fail]="item.preview.error || item.preview.converted === null"
          [attr.data-testid]="'einvoice-validation-' + item.rule.name">
          <strong>{{ item.rule.name }}</strong>
          <span class="einvoice-mapping__validation-value">{{ previewText(item.preview) }}</span>
          @if (item.preview.matchedBy === 'xpath') { <span class="einvoice-mapping__badge">XPath</span> }
          @if (item.preview.matchedBy === 'fallback') { <span class="einvoice-mapping__badge einvoice-mapping__badge--fallback">Regex fallback</span> }
        </li>
      }
    </ul>
    @for (collection of collections; track collection.name) {
      <h5>{{ collection.name }} (ilk 5 satır)</h5>
      <table class="einvoice-mapping__table" [attr.data-testid]="'einvoice-collection-preview-' + collection.name">
        <thead><tr>@for (column of collectionColumns(collection); track column) { <th>{{ column }}</th> }</tr></thead>
        <tbody>
          @for (row of collectionPreviewRows(collection); track $index) {
            <tr>@for (column of collectionColumns(collection); track column) { <td>{{ row[column] ?? '—' }}</td> }</tr>
          }
        </tbody>
      </table>
    }
    <details class="einvoice-mapping__json">
      <summary>Teknik JSON önizleme</summary>
      <pre data-testid="einvoice-draft-preview" aria-live="polite">{{ previewJson() }}</pre>
    </details>
  </div>
```

(c) `einvoice-mapping-editor.component.scss`'e ekle:

```scss
.einvoice-mapping__validation { list-style: none; margin: 0; padding: 0; }
.einvoice-mapping__validation-row {
  display: flex; align-items: center; gap: 0.5rem;
  padding: 0.25rem 0.5rem; border-left: 3px solid transparent;
}
.einvoice-mapping__validation-row--ok { border-left-color: #2e7d32; }
.einvoice-mapping__validation-row--fail { border-left-color: #c62828; }
.einvoice-mapping__validation-value { flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.einvoice-mapping__badge {
  font-size: 0.7rem; padding: 0.1rem 0.4rem; border-radius: 0.5rem;
  background: #e3f2fd; color: #1565c0;
}
.einvoice-mapping__badge--fallback { background: #fff3e0; color: #e65100; }
.einvoice-mapping__table { border-collapse: collapse; font-size: 0.8rem;
  th, td { border: 1px solid #ddd; padding: 0.2rem 0.5rem; } }
.einvoice-mapping__json summary { cursor: pointer; font-size: 0.8rem; }
.einvoice-mapping__empty { font-size: 0.85rem; opacity: 0.7; }
```

- [ ] **Step 6: Tüm Studio testlerini koş**

Run: `cd src/RPA.Studio` sonra `npm test -- --watch=false`
Expected: PASS. Mevcut spec'lerde `einvoice-draft-preview` testid'sine dayanan test varsa `<details>` içinde hâlâ mevcut — kırılmamalı; kırılan olursa selector'ı güncelle (davranış aynı).

- [ ] **Step 7: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.ts src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.html src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.scss src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.spec.ts
git commit -m "feat(studio): alan bazli gorsel dogrulama paneli + fallback girisleri

Kural satirlarinda bulunan deger + XPath/Regex-fallback rozeti,
koleksiyonlarda ilk 5 satir tablosu; ham JSON details icine tasindi.

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 7: Studio — Regex sihirbazı (regex bilmeyen kullanıcı için)

Yeni `regex-wizard` bileşeni: (1) hazır desen çipleri (Tarih, Tutar, VKN, TCKN, IBAN, Kur), (2) "örnekten üret" — kullanıcı örnek metinde çıkarmak istediği kısmı seçer, sistem öncesindeki sabit metni çapa yapıp named-group'lu desen üretir, (3) desenin Türkçe düz-dil açıklaması, (4) örnek metin üzerinde canlı eşleşme göstergesi. Mapping editöründeki Regex ve Fallback Regex alanlarının yanından açılır.

**Files:**
- Create: `src/RPA.Studio/src/app/studio/designer/properties/regex-wizard.model.ts`
- Create: `src/RPA.Studio/src/app/studio/designer/properties/regex-wizard.component.ts`
- Create: `src/RPA.Studio/src/app/studio/designer/properties/regex-wizard.component.html`
- Create: `src/RPA.Studio/src/app/studio/designer/properties/regex-wizard.component.scss`
- Modify: `src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.ts`
- Modify: `src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.html`
- Test (create): `src/RPA.Studio/src/app/studio/designer/properties/regex-wizard.model.spec.ts`
- Test: `src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.spec.ts`

**Interfaces:**
- Consumes: yok (saf model + standalone component).
- Produces:
  - Model: `REGEX_PRESETS: RegexPresetChip[]` (`{ id, label, pattern, group }`), `escapeRegex(text: string): string`, `generalizeSelection(selection: string): string`, `buildPatternFromSelection(text: string, start: number, end: number): { pattern: string; group: string }`, `explainRegex(pattern: string): string`.
  - Component: `<app-regex-wizard [sampleText]="..." (patternApply)="...">` — `patternApply: EventEmitter<{ pattern: string; group: string }>`.
  - Mapping editörü: `openRegexWizard(target: 'regex' | 'fallbackRegex'): void`, `applyWizardPattern(result: { pattern: string; group: string }): void`, `wizardTarget: 'regex' | 'fallbackRegex' | null`, `wizardSampleText(): string`.

- [ ] **Step 1: Failing model testlerini yaz**

Yeni dosya `regex-wizard.model.spec.ts`:

```typescript
import { buildPatternFromSelection, explainRegex, generalizeSelection, REGEX_PRESETS } from './regex-wizard.model';

describe('regex-wizard.model', () => {
  it('preset listesi IBAN ve Tarih içerir ve desenler derlenebilir', () => {
    const ids = REGEX_PRESETS.map(preset => preset.id);
    expect(ids).toContain('iban');
    expect(ids).toContain('date');
    for (const preset of REGEX_PRESETS) expect(() => new RegExp(preset.pattern)).not.toThrow();
  });

  it('generalizeSelection rakam dizilerini \\d+ ile genelleştirir ve özel karakterleri kaçışlar', () => {
    expect(generalizeSelection('TR12 3456')).toBe('TR\\d+\\s+\\d+');
    expect(generalizeSelection('1.234,56')).toBe('\\d+\\.\\d+,\\d+');
  });

  it('buildPatternFromSelection çapa + named group üretir ve örnek metinde eşleşir', () => {
    const text = 'Odeme IBAN: TR120001200012345678901234 uzerinden';
    const start = text.indexOf('TR12');
    const end = start + 'TR120001200012345678901234'.length;
    const result = buildPatternFromSelection(text, start, end);
    expect(result.group).toBe('deger');
    const match = new RegExp(result.pattern).exec(text);
    expect(match?.groups?.['deger']).toBe('TR120001200012345678901234');
  });

  it('buildPatternFromSelection öneksiz seçimde çapasız desen üretir', () => {
    const text = '16.07.2026 tarihli';
    const result = buildPatternFromSelection(text, 0, 10);
    const match = new RegExp(result.pattern).exec(text);
    expect(match?.groups?.['deger']).toBe('16.07.2026');
  });

  it('explainRegex bilinen yapı taşlarını Türkçe anlatır', () => {
    const explanation = explainRegex('KUR[:= ]+(?<deger>\\d+(?:[.,]\\d+)?)');
    expect(explanation).toContain('rakam');
    expect(explanation).toContain('Parantez');
  });
});
```

- [ ] **Step 2: FAIL gör**

Run: `cd src/RPA.Studio` sonra `npm test -- --watch=false`
Expected: modül bulunamadığından FAIL.

- [ ] **Step 3: `regex-wizard.model.ts`'i yaz**

```typescript
export interface RegexPresetChip {
  id: string;
  label: string;
  pattern: string;
  group: string;
}

export const REGEX_PRESETS: RegexPresetChip[] = [
  { id: 'date', label: 'Tarih', pattern: '(?<deger>\\d{2}[./]\\d{2}[./]\\d{4}|\\d{4}-\\d{2}-\\d{2})', group: 'deger' },
  { id: 'amount', label: 'Tutar', pattern: '(?<deger>\\d{1,3}(?:\\.\\d{3})*,\\d+|\\d+(?:[.,]\\d+)?)', group: 'deger' },
  { id: 'vkn', label: 'VKN (10 hane)', pattern: '(?<deger>\\b\\d{10}\\b)', group: 'deger' },
  { id: 'tckn', label: 'TCKN (11 hane)', pattern: '(?<deger>\\b\\d{11}\\b)', group: 'deger' },
  { id: 'iban', label: 'IBAN', pattern: '(?<deger>TR\\d{24})', group: 'deger' },
  { id: 'kur', label: 'Kur', pattern: '(?:KUR|Kur|kur)[:= ]+(?<deger>\\d+(?:[.,]\\d+)?)', group: 'deger' },
];

export function escapeRegex(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

/** Seçimi desene çevirir: rakam dizileri \d+, boşluk dizileri \s+, kalan karakterler literal. */
export function generalizeSelection(selection: string): string {
  return escapeRegex(selection)
    .replace(/\d+/g, '\\d+')
    .replace(/[ \t]+/g, '\\s+');
}

/**
 * Seçimin hemen öncesindeki (en çok 20 karakter) sabit metni çapa yapar,
 * seçimi genelleştirip 'deger' adlı gruba koyar.
 */
export function buildPatternFromSelection(text: string, start: number, end: number): { pattern: string; group: string } {
  const selection = text.slice(start, end);
  const prefix = text.slice(Math.max(0, start - 20), start).replace(/^\S*\s/, '').trimStart();
  const anchor = prefix ? `${escapeRegex(prefix).replace(/[ \t]+/g, '\\s+')}\\s*` : '';
  return { pattern: `${anchor}(?<deger>${generalizeSelection(selection.trim())})`, group: 'deger' };
}

/** Desendeki bilinen yapı taşlarını Türkçe cümlelerle açıklar. */
export function explainRegex(pattern: string): string {
  const parts: string[] = [];
  if (/\(\?<[^>]+>/.test(pattern)) parts.push('Parantez içindeki isimli bölüm, alınacak değerdir');
  if (pattern.includes('\\d')) parts.push('\\d bir rakamı temsil eder');
  if (pattern.includes('\\s')) parts.push('\\s bir boşluğu temsil eder');
  if (pattern.includes('\\b')) parts.push('\\b kelime sınırıdır (bitişik rakamları ayırır)');
  if (pattern.includes('+')) parts.push('+ "bir veya daha fazla tekrar" demektir');
  if (pattern.includes('?')) parts.push('? "isteğe bağlı" demektir');
  if (pattern.includes('|')) parts.push('| alternatifler arasında seçim yapar');
  if (pattern.includes('{')) parts.push('{n} tam n tekrar demektir (örn. \\d{10} = 10 rakam)');
  return parts.join('. ');
}
```

Not: `buildPatternFromSelection` içindeki `.replace(/^\S*\s/, '')` çapayı kelime sınırından başlatır (yarım kelime çapası üretmemek için); test 3'te `"Odeme IBAN: "` → çapa `"IBAN:"` civarı olur ve eşleşme korunur.

- [ ] **Step 4: Model testlerinin PASS ettiğini gör**

Run: `cd src/RPA.Studio` sonra `npm test -- --watch=false`
Expected: PASS. (`generalizeSelection('1.234,56')` beklentisi `'\\d+\\.\\d+,\\d+'` — escape sonrası nokta `\.`, rakamlar `\d+`; test FAIL ederse gerçek çıktıyı incele, davranışı değil beklentiyi düzeltme: önce escape sonra digit-replace sırası bu çıktıyı garanti eder.)

- [ ] **Step 5: Wizard bileşenini yaz**

`regex-wizard.component.ts`:

```typescript
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { buildPatternFromSelection, explainRegex, REGEX_PRESETS, RegexPresetChip } from './regex-wizard.model';

@Component({
  selector: 'app-regex-wizard',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './regex-wizard.component.html',
  styleUrls: ['./regex-wizard.component.scss'],
})
export class RegexWizardComponent {
  @Input() sampleText = '';
  @Output() readonly patternApply = new EventEmitter<{ pattern: string; group: string }>();

  readonly presets = REGEX_PRESETS;
  pattern = '';
  group = 'deger';
  error = '';

  usePreset(preset: RegexPresetChip): void {
    this.pattern = preset.pattern;
    this.group = preset.group;
    this.error = '';
  }

  generateFromSelection(textarea: HTMLTextAreaElement): void {
    const start = textarea.selectionStart;
    const end = textarea.selectionEnd;
    if (start === end) { this.error = 'Önce örnek metinde çıkarmak istediğin değeri seç.'; return; }
    const built = buildPatternFromSelection(textarea.value, start, end);
    this.pattern = built.pattern;
    this.group = built.group;
    this.error = '';
  }

  explanation(): string {
    return this.pattern ? explainRegex(this.pattern) : '';
  }

  /** Örnek metin üzerinde canlı deneme; hatalı desen kullanıcıya nazikçe bildirilir. */
  liveMatch(): string {
    if (!this.pattern || !this.sampleText) return '';
    try {
      const match = new RegExp(this.pattern).exec(this.sampleText);
      if (!match) return 'Örnek metinde eşleşme yok.';
      const value = this.group ? match.groups?.[this.group] ?? match[0] : match[0];
      return `Bulunan: ${value}`;
    } catch {
      return 'Desen geçersiz.';
    }
  }

  apply(): void {
    if (!this.pattern) { this.error = 'Önce bir desen seç veya üret.'; return; }
    this.patternApply.emit({ pattern: this.pattern, group: this.group });
  }
}
```

`regex-wizard.component.html`:

```html
<section class="regex-wizard" data-testid="regex-wizard" aria-label="Regex yardımcısı">
  <h5>Regex yardımcısı</h5>
  <p class="regex-wizard__hint">Regex bilmene gerek yok: hazır bir desen seç ya da aşağıdaki örnek metinde çıkarmak istediğin değeri fareyle seçip "Seçimden üret"e bas.</p>
  <div class="regex-wizard__chips" role="group" aria-label="Hazır desenler">
    @for (preset of presets; track preset.id) {
      <button type="button" class="regex-wizard__chip" [attr.data-testid]="'regex-preset-' + preset.id"
        (click)="usePreset(preset)">{{ preset.label }}</button>
    }
  </div>
  <label for="regex-wizard-sample">Örnek metin</label>
  <textarea #sample id="regex-wizard-sample" data-testid="regex-wizard-sample" rows="4" readonly>{{ sampleText }}</textarea>
  <button type="button" data-testid="regex-wizard-generate" (click)="generateFromSelection(sample)">Seçimden üret</button>
  @if (pattern) {
    <label for="regex-wizard-pattern">Üretilen desen</label>
    <input id="regex-wizard-pattern" data-testid="regex-wizard-pattern" [value]="pattern" readonly />
    <p class="regex-wizard__explain" data-testid="regex-wizard-explanation">{{ explanation() }}</p>
    <p class="regex-wizard__live" data-testid="regex-wizard-live" aria-live="polite">{{ liveMatch() }}</p>
  }
  @if (error) { <p class="regex-wizard__error" role="alert">{{ error }}</p> }
  <button type="button" data-testid="regex-wizard-apply" (click)="apply()">Alana uygula</button>
</section>
```

`regex-wizard.component.scss`:

```scss
.regex-wizard {
  display: flex; flex-direction: column; gap: 0.4rem;
  border: 1px solid #ddd; border-radius: 0.5rem; padding: 0.75rem;
  textarea, input { font-family: monospace; font-size: 0.8rem; }
}
.regex-wizard__chips { display: flex; flex-wrap: wrap; gap: 0.3rem; }
.regex-wizard__chip { border-radius: 1rem; padding: 0.2rem 0.7rem; cursor: pointer; }
.regex-wizard__hint, .regex-wizard__explain { font-size: 0.8rem; opacity: 0.8; }
.regex-wizard__live { font-size: 0.85rem; font-weight: 600; }
.regex-wizard__error { color: #c62828; font-size: 0.8rem; }
```

- [ ] **Step 6: Mapping editörüne entegrasyon için failing test yaz**

`einvoice-mapping-editor.component.spec.ts`'e ekle:

```typescript
it('regex sihirbazı hedef alana desen ve grubu yazar', () => {
  component.openRegexWizard('fallbackRegex');
  expect(component.wizardTarget).toBe('fallbackRegex');
  component.applyWizardPattern({ pattern: 'TR\\d{24}', group: 'deger' });
  expect(component.draft.fallbackRegex).toBe('TR\\d{24}');
  expect(component.draft.fallbackGroup).toBe('deger');
  expect(component.wizardTarget).toBeNull();
});

it('regex sihirbazı regex hedefinde regex/group alanlarına yazar', () => {
  component.openRegexWizard('regex');
  component.applyWizardPattern({ pattern: 'IBAN[: ]+(?<deger>TR\\d{24})', group: 'deger' });
  expect(component.draft.regex).toBe('IBAN[: ]+(?<deger>TR\\d{24})');
  expect(component.draft.group).toBe('deger');
});
```

- [ ] **Step 7: FAIL gör**

Run: `cd src/RPA.Studio` sonra `npm test -- --watch=false`
Expected: `openRegexWizard` yok → derleme FAIL.

- [ ] **Step 8: Mapping editörüne sihirbazı bağla**

`einvoice-mapping-editor.component.ts`:

(a) Import ve `imports` dizisi: component'in `@Component` dekoratörüne `imports: [RegexWizardComponent]` ekle (standalone; dekoratörde `imports` alanı yoksa oluştur) ve dosya başına `import { RegexWizardComponent } from './regex-wizard.component';` ekle.

(b) Alanlar ve metodlar:

```typescript
wizardTarget: 'regex' | 'fallbackRegex' | null = null;

openRegexWizard(target: 'regex' | 'fallbackRegex'): void {
  this.wizardTarget = this.wizardTarget === target ? null : target;
}

applyWizardPattern(result: { pattern: string; group: string }): void {
  if (this.wizardTarget === 'regex') {
    this.draft = { ...this.draft, regex: result.pattern, group: result.group };
  } else if (this.wizardTarget === 'fallbackRegex') {
    this.draft = { ...this.draft, fallbackRegex: result.pattern, fallbackGroup: result.group };
  }
  this.wizardTarget = null;
  this.cancelRegexPreview();
}

/** Sihirbaza verilecek örnek metin: XPath'in bulduğu ham değer; yoksa belgenin notları/metni. */
wizardSampleText(): string {
  if (!this.sampleDocument) return '';
  if (this.wizardTarget === 'regex') {
    const base = previewRule({ ...this.draft, regex: null, group: null, type: 'string' }, this.sampleDocument);
    if (typeof base.raw === 'string') return base.raw;
    if (Array.isArray(base.raw)) return base.raw.join('\n');
  }
  return this.sampleDocument.documentElement.textContent?.trim() ?? '';
}
```

(c) `einvoice-mapping-editor.component.html` — Task 6'da eklenen fallback group input'undan sonra:

```html
    <div>
      <button type="button" data-testid="open-wizard-regex" (click)="openRegexWizard('regex')">Regex yardımcısı</button>
      <button type="button" data-testid="open-wizard-fallback" (click)="openRegexWizard('fallbackRegex')">Fallback yardımcısı</button>
    </div>
    @if (wizardTarget) {
      <app-regex-wizard [sampleText]="wizardSampleText()" (patternApply)="applyWizardPattern($event)"></app-regex-wizard>
    }
```

- [ ] **Step 9: PASS gör**

Run: `cd src/RPA.Studio` sonra `npm test -- --watch=false`
Expected: PASS.

- [ ] **Step 10: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/properties/regex-wizard.model.ts src/RPA.Studio/src/app/studio/designer/properties/regex-wizard.model.spec.ts src/RPA.Studio/src/app/studio/designer/properties/regex-wizard.component.ts src/RPA.Studio/src/app/studio/designer/properties/regex-wizard.component.html src/RPA.Studio/src/app/studio/designer/properties/regex-wizard.component.scss src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.ts src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.html src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.spec.ts
git commit -m "feat(studio): regex sihirbazi — hazir desen cipleri + secimden uretme

Regex bilmeyen kullanici icin: Tarih/Tutar/VKN/TCKN/IBAN/Kur presetleri,
ornek metinde secimden capa+named-group desen uretimi, Turkce aciklama,
canli eslesme onizlemesi. Regex ve fallback alanlarina uygulanabilir.

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 8: Designer — profil için "daha yeni sürüm var" uyarısı

Spec (2026-07-16-project-einvoice-profiles-design.md, Bölüm 8 madde 6): "Profilin daha yeni sürümü varsa uyarı gösterir; kullanıcı onayı olmadan sürümü değiştirmez." Bugün profil seçilince otomatik son sürüm atanıyor ama var olan bir node açıldığında yeni sürüm kontrolü yapılmıyor.

**Files:**
- Modify: `src/RPA.Studio/src/app/studio/designer/properties/generic-property.component.ts`
- Modify: `src/RPA.Studio/src/app/studio/designer/properties/generic-property.component.html`
- Test: `src/RPA.Studio/src/app/studio/designer/properties/generic-property.component.spec.ts`

**Interfaces:**
- Consumes: `EInvoiceProfileService.versions(projectId, profileId): Observable<EInvoiceProfileVersion[]>` (mevcut), `properties['profileId'|'profileVersion'|'projectId'|'outputSchemaJson']`.
- Produces: `einvoiceNewerVersion: number | null` alanı, `applyLatestEInvoiceVersion(): void` metodu, `data-testid="einvoice-newer-version"` uyarı bloğu ve `data-testid="einvoice-apply-latest"` butonu.

- [ ] **Step 1: Failing testleri yaz**

`generic-property.component.spec.ts`'e ekle (dosyadaki mevcut `EInvoice.ReadProfile` test kurulum desenini — `component.activityType` set + `http.expectOne('/api/activities/...')` flush — aynen izle):

```typescript
it('node eski profil sürümündeyse yeni sürüm uyarısı gösterir', () => {
  component.properties = { projectId: 'proj-1', profileId: 'prof-1', profileVersion: 1 };
  component.activityType = 'EInvoice.ReadProfile';
  http.expectOne('/api/activities/EInvoice.ReadProfile').flush({
    activityId: 'EInvoice.ReadProfile',
    displayName: 'E-Fatura Profil Oku',
    inputs: [
      { name: 'profileId', type: 'string', required: true, pickerKind: 'einvoice-profile' },
      { name: 'profileVersion', type: 'int', required: true },
    ],
  });
  http.expectOne('/api/projects/proj-1/einvoice-profiles/prof-1/versions').flush([
    { profileId: 'prof-1', version: 2, outputSchemaJson: '{"type":"object"}', publishedAt: '2026-07-16T00:00:00Z' },
    { profileId: 'prof-1', version: 1, outputSchemaJson: '{"type":"object"}', publishedAt: '2026-07-15T00:00:00Z' },
  ]);
  fixture.detectChanges();

  expect(component.einvoiceNewerVersion).toBe(2);
  const warning = fixture.nativeElement.querySelector('[data-testid="einvoice-newer-version"]');
  expect(warning).toBeTruthy();
});

it('son sürüme geç butonu sürümü ve şemayı günceller', () => {
  component.properties = { projectId: 'proj-1', profileId: 'prof-1', profileVersion: 1 };
  component.einvoiceProfileVersions = [
    { profileId: 'prof-1', version: 2, outputSchemaJson: '{"v":2}', publishedAt: '2026-07-16T00:00:00Z' } as any,
    { profileId: 'prof-1', version: 1, outputSchemaJson: '{"v":1}', publishedAt: '2026-07-15T00:00:00Z' } as any,
  ];
  const emitted: Record<string, unknown>[] = [];
  component.propertiesChange.subscribe(properties => emitted.push(properties));

  component.applyLatestEInvoiceVersion();

  expect(emitted[0]['profileVersion']).toBe(2);
  expect(emitted[0]['outputSchemaJson']).toBe('{"v":2}');
  expect(component.einvoiceNewerVersion).toBeNull();
});
```

Not: `EInvoiceProfileVersion` modelinin alan adları `src/RPA.Studio/src/app/shared/models/einvoice-profile.model.ts`'de tanımlıdır; test mock'larını oradaki gerçek alan adlarına göre uyarla (örn. `id`, `publishedBy` gibi zorunlu alanlar varsa ekle).

- [ ] **Step 2: FAIL gör**

Run: `cd src/RPA.Studio` sonra `npm test -- --watch=false`
Expected: `einvoiceNewerVersion` yok → derleme FAIL.

- [ ] **Step 3: Component'i güncelle**

`generic-property.component.ts`:

(a) Alan ekle:

```typescript
einvoiceNewerVersion: number | null = null;
```

(b) `loadMetadata` başarılı `next` callback'inin sonuna ekle:

```typescript
if (activityType === 'EInvoice.ReadProfile' || activityType === 'EInvoice.ReadProfileBatch') {
  this.loadEInvoiceVersionInfo();
}
```

(c) Yeni private metodlar:

```typescript
/** Node var olan profileId ile açıldığında sürüm listesini çekip yeni sürüm kontrolü yapar. */
private loadEInvoiceVersionInfo(): void {
  const projectId = String(this.properties['projectId'] ?? '').trim();
  const profileId = String(this.properties['profileId'] ?? '').trim();
  if (!projectId || !profileId) return;
  this.einvoiceProfiles.versions(projectId, profileId).subscribe({
    next: versions => {
      this.einvoiceProfileVersions = versions;
      this.refreshEInvoiceVersionWarning();
      this.cdr.markForCheck();
    },
    error: () => { /* uyarı üretilemedi; node çalışmaya devam eder */ },
  });
}

private refreshEInvoiceVersionWarning(): void {
  const latest = [...this.einvoiceProfileVersions].sort((a, b) => b.version - a.version)[0];
  const current = Number(this.properties['profileVersion'] ?? 0);
  this.einvoiceNewerVersion = latest && current > 0 && latest.version > current ? latest.version : null;
}
```

(d) Public metod:

```typescript
/** Kullanıcı onayıyla node'u en son yayınlanmış sürüme taşır (spec 8.6: otomatik geçiş yok). */
applyLatestEInvoiceVersion(): void {
  const latest = [...this.einvoiceProfileVersions].sort((a, b) => b.version - a.version)[0];
  if (!latest) return;
  const next = { ...this.properties, profileVersion: latest.version, outputSchemaJson: latest.outputSchemaJson };
  this.properties = next;
  this.propertiesChange.emit(next);
  this.refreshEInvoiceVersionWarning();
}
```

(e) `selectEInvoiceProfile` içindeki versions `next` callback'inin sonuna (`this.cdr.markForCheck();` öncesine) `this.refreshEInvoiceVersionWarning();` ekle.

(f) `generic-property.component.html` — `einvoiceProfileError` bloğundan sonra ekle:

```html
              @if (einvoiceNewerVersion) {
                <p class="generic-property__warning" data-testid="einvoice-newer-version" role="status">
                  Bu profilin daha yeni bir sürümü yayınlandı (v{{ einvoiceNewerVersion }}).
                  Node şu an v{{ properties['profileVersion'] }} kullanıyor.
                  <button type="button" data-testid="einvoice-apply-latest" (click)="applyLatestEInvoiceVersion()">
                    Son sürüme geç
                  </button>
                </p>
              }
```

(g) `generic-property.component.scss`'e ekle:

```scss
.generic-property__warning {
  background: #fff8e1; border-left: 3px solid #f9a825;
  padding: 0.4rem 0.6rem; font-size: 0.8rem;
  button { margin-left: 0.5rem; }
}
```

- [ ] **Step 4: PASS gör**

Run: `cd src/RPA.Studio` sonra `npm test -- --watch=false`
Expected: PASS (yeni 2 + mevcut generic-property testleri).

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/properties/generic-property.component.ts src/RPA.Studio/src/app/studio/designer/properties/generic-property.component.html src/RPA.Studio/src/app/studio/designer/properties/generic-property.component.scss src/RPA.Studio/src/app/studio/designer/properties/generic-property.component.spec.ts
git commit -m "feat(studio): designer'da e-fatura profil yeni surum uyarisi

Node eski surume sabitliyse uyari + kullanici onayli 'son surume gec'
butonu (spec 8.6: otomatik surum degisikligi yok).

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 9: Adresleme editörü yerleşim sadeleştirmesi — adımlı akış

**Bu görev Task 6 ve 7'den SONRA çalıştırılmalıdır** (onların eklediği HTML parçalarını nihai yerleşime taşır).

Bugünkü sorun: dört panel (XML ağacı | Eşleme kuralı | Satır koleksiyonu | Önizleme) yan yana ve hepsi aynı anda görünür; kural formunda 8+ alan üst üste; koleksiyon akışı ayrı bir üçüncü panelde. Kullanıcının zihinsel modeli ise sıralı: *örnek yükle → alanları bağla → doğrula → yayınla*.

**Tasarım kararları:**

1. **Üç adımlı akış (stepper):** ① Örnek XML → ② Alanları bağla → ③ Doğrula. Adımlar arası serbest geçiş (buton), XML yüklenince otomatik adım 2. Ekranın imzası bu akıştır; başka süsleme yok.
2. **Adım 2 = iki sütun:** solda XML ağacı (kaynak), sağda tek **"Alan kartı"**. Ağaçta tıklanan öğe kartı doldurur — "tıkla → gör → ekle" bağlantısı görsel olarak tek hatta.
3. **Progressive disclosure:** Alan kartında yalnız temel alanlar görünür (Alan adı, Veri tipi, Nereye eklensin?, Value XPath, Zorunlu/Çoklu). Kaynak türü, Scope, Regex, Fallback, Grup ve sihirbazlar `<details>` içinde **"Gelişmiş"** başlığı altında — regex bilmeyen kullanıcı hiç görmez.
4. **Üçüncü panel kalkar:** koleksiyon kavramı, alan kartındaki **"Nereye eklensin?"** seçimine iner (`Fatura alanı` | mevcut satır dizileri) + "+ Yeni satır dizisi" mini formu. Tek "Alanı ekle" butonu hedefe göre yönlendirir.
5. **Doğrulama = adım 3:** Task 6'nın alan-satırı/tablo görünümü kendi adımında tam genişlik kullanır; teknik JSON `<details>` içinde kalır.
6. Dış sayfada (`einvoice-profiles`) taslak JSON `textarea`'sı da `<details>`'a iner (spec: "JSON textarea teknik/ikincil bilgi olarak kalmalı").

**Files:**
- Modify: `src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.ts`
- Modify: `src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.html` (tam yeniden yazım — aşağıda komple içerik)
- Modify: `src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.scss`
- Modify: `src/RPA.Studio/src/app/studio/projects/einvoice-profiles/einvoice-profiles.component.html`
- Test: `src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.spec.ts`

**Interfaces:**
- Consumes: Task 6'nın `savedRulePreviews`/`collectionPreviewRows`/`collectionColumns`/`previewText` metodları; Task 7'nin `openRegexWizard`/`applyWizardPattern`/`wizardSampleText`/`wizardTarget` API'si; mevcut `addDraftRule`, `addDraftAsCollectionField`, `addCollectionFromDraft`.
- Produces: `activeStep: 1 | 2 | 3`, `setStep(step: 1 | 2 | 3): void`, `draftTarget: string` (`'root'` veya koleksiyon adı), `addDraft(): void`, `newCollectionOpen: boolean`. Mevcut public metodlar/testid'ler korunur: `einvoice-tree-node`, `einvoice-sample`, `collection-name`, `collection-scope`, `add-collection`, `selected-collection` (artık hedef seçici), `einvoice-rule-*`, `einvoice-fallback-*`, `open-wizard-*`, `einvoice-validation-*`, `einvoice-draft-preview`. Kaldırılan testid: `add-line-field` (yerine `einvoice-add-rule` + `draftTarget`).

- [ ] **Step 1: Failing testleri yaz**

`einvoice-mapping-editor.component.spec.ts`'e ekle (Task 6'daki `PREVIEW_SAMPLE` sabitini kullanır):

```typescript
it('örnek XML yüklenince otomatik 2. adıma geçer', () => {
  expect(component.activeStep).toBe(1);
  component.loadSampleXml(PREVIEW_SAMPLE);
  expect(component.activeStep).toBe(2);
});

it('hedef fatura alanı iken addDraft kök kurala ekler', () => {
  component.draftTarget = 'root';
  component.draft = { name: 'faturaNo', source: 'XPath', valueXPath: '/Invoice/cbc:ID', type: 'string', required: false, multiple: false };
  component.addDraft();
  expect(component.rules.length).toBe(1);
  expect(component.rules[0].name).toBe('faturaNo');
});

it('hedef satır dizisi iken addDraft alanı koleksiyona ekler', () => {
  component.addCollection('satirlar', '//cac:InvoiceLine');
  component.draftTarget = 'satirlar';
  component.draft = { name: 'Miktar', source: 'XPath', valueXPath: './cbc:InvoicedQuantity', type: 'integer', required: false, multiple: false };
  component.addDraft();
  expect(component.collections[0].fields.length).toBe(1);
  expect(component.collections[0].fields[0].name).toBe('Miktar');
  expect(component.rules.length).toBe(0);
});
```

- [ ] **Step 2: FAIL gör**

Run: `cd src/RPA.Studio` sonra `npm test -- --watch=false`
Expected: `activeStep`/`draftTarget`/`addDraft` yok → derleme FAIL.

- [ ] **Step 3: Component TS'e adım/hedef durumunu ekle**

`einvoice-mapping-editor.component.ts`'e alanlar ve metodlar:

```typescript
activeStep: 1 | 2 | 3 = 1;
draftTarget = 'root';
newCollectionOpen = false;

setStep(step: 1 | 2 | 3): void {
  this.activeStep = step;
  this.cdr?.markForCheck();
}

/** Alan kartındaki tek "Alanı ekle" butonu: hedefe göre kök kurala veya koleksiyona yazar. */
addDraft(): void {
  if (this.draftTarget === 'root') {
    this.addDraftRule();
    return;
  }
  this.selectedCollectionName = this.draftTarget;
  this.addDraftAsCollectionField();
  this.draft = { ...this.draft, name: '' };
}
```

`loadSampleXml` metodunun sonuna (mevcut `this.cdr?.markForCheck();` satırından önce) ekle:

```typescript
if (this.tree.length) this.activeStep = 2;
```

- [ ] **Step 4: Metod testlerinin PASS ettiğini gör**

Run: `cd src/RPA.Studio` sonra `npm test -- --watch=false`
Expected: yeni 3 test PASS (DOM'a bağlı eski testler bir sonraki adımda ele alınır).

- [ ] **Step 5: HTML'i adımlı yerleşime yeniden yaz**

`einvoice-mapping-editor.component.html` dosyasının **tamamını** şununla değiştir (Task 6 doğrulama paneli ve Task 7 sihirbaz entegrasyonu bu nihai içeriğe taşınmıştır):

```html
<section class="einvoice-mapping" aria-label="E-fatura eşleme editörü">
  <nav class="einvoice-mapping__steps" aria-label="Adresleme adımları">
    <button type="button" data-testid="einvoice-step-1"
      class="einvoice-mapping__step" [class.einvoice-mapping__step--active]="activeStep === 1"
      [attr.aria-current]="activeStep === 1 ? 'step' : null" (click)="setStep(1)">1. Örnek XML</button>
    <button type="button" data-testid="einvoice-step-2"
      class="einvoice-mapping__step" [class.einvoice-mapping__step--active]="activeStep === 2"
      [attr.aria-current]="activeStep === 2 ? 'step' : null" (click)="setStep(2)">2. Alanları bağla</button>
    <button type="button" data-testid="einvoice-step-3"
      class="einvoice-mapping__step" [class.einvoice-mapping__step--active]="activeStep === 3"
      [attr.aria-current]="activeStep === 3 ? 'step' : null" (click)="setStep(3)">3. Doğrula</button>
  </nav>

  @if (activeStep === 1) {
    <div class="einvoice-mapping__panel" data-testid="einvoice-sample-panel">
      <h4>Örnek XML seç</h4>
      <p class="einvoice-mapping__hint">Alanları üzerinde işaretleyeceğin örnek bir e-fatura XML dosyası yükle. Dosya yalnızca tarayıcında kalır; sunucuya gönderilmez.</p>
      <label for="einvoice-sample">Örnek XML seç</label>
      <input id="einvoice-sample" type="file" accept=".xml,text/xml,application/xml" (change)="onSampleFileSelected($event)" />
      @if (sampleError) { <p role="alert">{{ sampleError }}</p> }
      @if (tree.length) {
        <button type="button" data-testid="einvoice-goto-fields" (click)="setStep(2)">Alanları bağla →</button>
      }
    </div>
  }

  @if (activeStep === 2) {
    <div class="einvoice-mapping__workbench">
      <div class="einvoice-mapping__panel" data-testid="einvoice-tree-panel">
        <h4>XML ağacı</h4>
        <p class="einvoice-mapping__hint">Bir öğeye tıkla; yolu sağdaki alan kartına yazılır. ×N işaretli öğeler tekrar eder (satır adayı).</p>
        @if (!tree.length) {
          <p class="einvoice-mapping__empty">Önce 1. adımda örnek XML yükle.</p>
        }
        <ul class="einvoice-mapping__tree" aria-label="XML elemanları">
          @for (item of flatTree(); track item.node.element) {
            <li [style.padding-left.rem]="item.depth">
              @if (item.node.children.length) {
                <button type="button" [attr.data-toggle-name]="item.node.name" [attr.aria-expanded]="isExpanded(item.node)"
                  [attr.aria-label]="item.node.name + ' dalını aç veya kapat'" (click)="toggleNode(item.node)">▸</button>
              }
              <button type="button" data-testid="einvoice-tree-node" [attr.data-node-name]="item.node.name"
                [attr.aria-expanded]="item.node.children.length ? isExpanded(item.node) : null"
                (click)="selectNode(item.node)" (keydown)="onTreeKeydown($event, item.node)">
                {{ item.node.name }} @if (nodeSample(item.node)) { <span>{{ nodeSample(item.node) }}</span> }
                @if (repeatedCount(item.node) > 1) { <span>×{{ repeatedCount(item.node) }}</span> }
              </button>
            </li>
          }
        </ul>
      </div>

      <div class="einvoice-mapping__panel" data-testid="einvoice-rule-panel">
        <h4>Alan kartı</h4>
        <label for="einvoice-rule-name">Alan adı</label>
        <input id="einvoice-rule-name" [value]="draft.name" (input)="updateDraft('name', $any($event.target).value)" placeholder="örn. faturaTarihi" />

        <label for="einvoice-rule-type">Veri tipi</label>
        <select id="einvoice-rule-type" [value]="draft.type" (change)="updateDraft('type', $any($event.target).value)">
          <option value="string">Metin</option><option value="integer">Tamsayı</option><option value="decimal">Ondalık</option><option value="date">Tarih</option><option value="boolean">Boolean</option>
        </select>

        <label for="einvoice-rule-target">Nereye eklensin?</label>
        <select id="einvoice-rule-target" data-testid="selected-collection"
          [value]="draftTarget" (change)="draftTarget = $any($event.target).value">
          <option value="root">Fatura alanı</option>
          @for (collection of collections; track collection.name) {
            <option [value]="collection.name">{{ collection.name }} (satır alanı)</option>
          }
        </select>
        <button type="button" class="einvoice-mapping__link" data-testid="einvoice-new-collection"
          (click)="newCollectionOpen = !newCollectionOpen">+ Yeni satır dizisi</button>
        @if (newCollectionOpen) {
          <div class="einvoice-mapping__subform" data-testid="collection-panel">
            <label for="einvoice-collection-name">Dizi adı</label>
            <input id="einvoice-collection-name" data-testid="collection-name"
              [value]="collectionName" (input)="collectionName = $any($event.target).value" placeholder="satirlar" />
            <label for="einvoice-collection-scope">Satır scope XPath</label>
            <input id="einvoice-collection-scope" data-testid="collection-scope"
              [value]="collectionScopeXPath" (input)="collectionScopeXPath = $any($event.target).value" placeholder="//cac:InvoiceLine" />
            <p class="einvoice-mapping__hint">Ağaçta tekrar eden bir öğeye tıklarsan scope otomatik dolar.</p>
            <button type="button" data-testid="add-collection"
              (click)="addCollectionFromDraft(); newCollectionOpen = false; draftTarget = collections.length ? collections[collections.length - 1].name : 'root'">
              Satır dizisi oluştur
            </button>
          </div>
        }

        <label for="einvoice-rule-xpath">Value XPath</label>
        <input id="einvoice-rule-xpath" [value]="draft.valueXPath ?? ''" (input)="updateDraft('valueXPath', $any($event.target).value)" placeholder="ağaçtan tıklayınca dolar" />

        <label><input type="checkbox" [checked]="draft.required" (change)="updateDraft('required', $any($event.target).checked)" /> Zorunlu</label>
        <label><input type="checkbox" [checked]="draft.multiple" (change)="updateDraft('multiple', $any($event.target).checked)" /> Çoklu</label>

        <details class="einvoice-mapping__advanced">
          <summary>Gelişmiş (kaynak, regex, fallback)</summary>
          <div>
            <button type="button" data-testid="einvoice-preset-kur" (click)="applyPreset('kur')">Kur preset</button>
            <button type="button" data-testid="einvoice-preset-iban" (click)="applyPreset('iban')">IBAN preset</button>
            <button type="button" data-testid="einvoice-preset-note" (click)="applyPreset('note')">Fatura notu</button>
          </div>
          <label for="einvoice-rule-source">Kaynak</label>
          <select id="einvoice-rule-source" [value]="draft.source" (change)="updateDraft('source', $any($event.target).value)">
            <option>Standard</option><option>XPath</option><option>InvoiceNotes</option><option>LineNotes</option>
          </select>
          <label for="einvoice-rule-scope">Scope XPath</label>
          <input id="einvoice-rule-scope" [value]="draft.scopeXPath ?? ''" (input)="updateDraft('scopeXPath', $any($event.target).value)" />
          <label for="einvoice-rule-regex">Regex (XPath sonucunu süzer)</label>
          <input id="einvoice-rule-regex" [value]="draft.regex ?? ''" (input)="updateDraft('regex', $any($event.target).value)" />
          <label for="einvoice-rule-group">Group</label>
          <input id="einvoice-rule-group" [value]="draft.group ?? ''" (input)="updateDraft('group', $any($event.target).value)" />
          <label for="einvoice-rule-fallback-regex">Bulunamazsa regex ile ara (fallback)</label>
          <input id="einvoice-rule-fallback-regex" data-testid="einvoice-fallback-regex"
            [value]="draft.fallbackRegex ?? ''" (input)="updateDraft('fallbackRegex', $any($event.target).value)"
            placeholder="örn. TR\d{24}" />
          <label for="einvoice-rule-fallback-group">Fallback grup adı</label>
          <input id="einvoice-rule-fallback-group" data-testid="einvoice-fallback-group"
            [value]="draft.fallbackGroup ?? ''" (input)="updateDraft('fallbackGroup', $any($event.target).value)" />
          <div>
            <button type="button" data-testid="open-wizard-regex" (click)="openRegexWizard('regex')">Regex yardımcısı</button>
            <button type="button" data-testid="open-wizard-fallback" (click)="openRegexWizard('fallbackRegex')">Fallback yardımcısı</button>
          </div>
          @if (wizardTarget) {
            <app-regex-wizard [sampleText]="wizardSampleText()" (patternApply)="applyWizardPattern($event)"></app-regex-wizard>
          }
        </details>

        <button type="button" class="einvoice-mapping__primary" data-testid="einvoice-add-rule"
          (click)="editingIndex === null ? addDraft() : saveDraftRule()">
          {{ editingIndex === null ? 'Alanı ekle' : 'Alanı güncelle' }}
        </button>

        <h5>Tanımlı alanlar</h5>
        <ul aria-label="Kaydedilmiş eşleme kuralları">
          @for (rule of rules; track $index) {
            <li><span>{{ rule.name }}</span>
              <button type="button" (click)="editRule($index)">Düzenle</button>
              <button type="button" (click)="removeRule($index)">Sil</button>
            </li>
          }
        </ul>
        @for (collection of collections; track collection.name) {
          <p class="einvoice-mapping__collection-title"><strong>{{ collection.name }}</strong> <span>{{ collection.scopeXPath }}</span></p>
          <ul aria-label="Satır alanları">
            @for (field of collection.fields; track field.name) {
              <li>{{ collection.name }}.{{ field.name }} ← {{ field.valueXPath }}</li>
            }
          </ul>
        }
      </div>
    </div>
  }

  @if (activeStep === 3) {
    <div class="einvoice-mapping__panel" data-testid="einvoice-preview-panel">
      <h4>Doğrulama</h4>
      @if (!tree.length) {
        <p class="einvoice-mapping__empty">Bulunan değerleri görmek için 1. adımda örnek XML yükleyin.</p>
      }
      <ul class="einvoice-mapping__validation" aria-label="Alan doğrulama sonuçları">
        @for (item of savedRulePreviews(); track item.rule.name) {
          <li class="einvoice-mapping__validation-row"
            [class.einvoice-mapping__validation-row--ok]="!item.preview.error && item.preview.converted !== null"
            [class.einvoice-mapping__validation-row--fail]="item.preview.error || item.preview.converted === null"
            [attr.data-testid]="'einvoice-validation-' + item.rule.name">
            <strong>{{ item.rule.name }}</strong>
            <span class="einvoice-mapping__validation-value">{{ previewText(item.preview) }}</span>
            @if (item.preview.matchedBy === 'xpath') { <span class="einvoice-mapping__badge">XPath</span> }
            @if (item.preview.matchedBy === 'fallback') { <span class="einvoice-mapping__badge einvoice-mapping__badge--fallback">Regex fallback</span> }
          </li>
        }
      </ul>
      @for (collection of collections; track collection.name) {
        <h5>{{ collection.name }} (ilk 5 satır)</h5>
        <table class="einvoice-mapping__table" [attr.data-testid]="'einvoice-collection-preview-' + collection.name">
          <thead><tr>@for (column of collectionColumns(collection); track column) { <th>{{ column }}</th> }</tr></thead>
          <tbody>
            @for (row of collectionPreviewRows(collection); track $index) {
              <tr>@for (column of collectionColumns(collection); track column) { <td>{{ row[column] ?? '—' }}</td> }</tr>
            }
          </tbody>
        </table>
      }
      <details class="einvoice-mapping__json">
        <summary>Teknik JSON önizleme</summary>
        <pre data-testid="einvoice-draft-preview" aria-live="polite">{{ previewJson() }}</pre>
      </details>
    </div>
  }
</section>
```

- [ ] **Step 6: SCSS'e adım/yerleşim stillerini ekle**

`einvoice-mapping-editor.component.scss`'e ekle (Task 6 stilleri kalır):

```scss
.einvoice-mapping__steps {
  display: flex; gap: 0.25rem; margin-bottom: 0.75rem;
  border-bottom: 1px solid #ddd;
}
.einvoice-mapping__step {
  border: none; background: none; padding: 0.5rem 0.9rem; cursor: pointer;
  border-bottom: 2px solid transparent; font-weight: 500; opacity: 0.65;
}
.einvoice-mapping__step--active { border-bottom-color: #1565c0; opacity: 1; }
.einvoice-mapping__workbench {
  display: grid; grid-template-columns: minmax(240px, 1fr) minmax(320px, 1.2fr); gap: 1rem;
  @media (max-width: 900px) { grid-template-columns: 1fr; }
}
.einvoice-mapping__advanced {
  margin: 0.5rem 0;
  summary { cursor: pointer; font-size: 0.85rem; opacity: 0.8; }
  > div, > label, > input, > select { margin-top: 0.3rem; }
}
.einvoice-mapping__subform {
  border: 1px dashed #bbb; border-radius: 0.4rem; padding: 0.5rem; margin: 0.4rem 0;
  display: flex; flex-direction: column; gap: 0.25rem;
}
.einvoice-mapping__link { background: none; border: none; color: #1565c0; cursor: pointer; padding: 0.2rem 0; text-align: left; }
.einvoice-mapping__primary { font-weight: 600; padding: 0.4rem 0.9rem; }
.einvoice-mapping__hint { font-size: 0.8rem; opacity: 0.75; }
.einvoice-mapping__collection-title { margin: 0.4rem 0 0.1rem; font-size: 0.85rem;
  span { opacity: 0.6; margin-left: 0.4rem; font-family: monospace; font-size: 0.75rem; } }
```

- [ ] **Step 7: Dış sayfadaki JSON textarea'yı ikincil hale getir**

`einvoice-profiles.component.html` içinde şu bloğu:

```html
        <textarea
          class="einvoice-profiles__draft"
          data-testid="profile-draft"
          [ngModel]="draftJson()"
          (ngModelChange)="draftJson.set($event)"
          spellcheck="false"
        ></textarea>
```

şununla değiştir:

```html
        <details class="einvoice-profiles__json-details">
          <summary>Teknik JSON (gelişmiş)</summary>
          <textarea
            class="einvoice-profiles__draft"
            data-testid="profile-draft"
            [ngModel]="draftJson()"
            (ngModelChange)="draftJson.set($event)"
            spellcheck="false"
          ></textarea>
        </details>
```

(`data-testid="profile-draft"` DOM'da kalır; kapalı `<details>` içeriği querySelector ile hâlâ bulunur — mevcut testler kırılmaz.)

- [ ] **Step 8: Tüm Studio testlerini koş, DOM'a bağlı eski akış testlerini güncelle**

Run: `cd src/RPA.Studio` sonra `npm test -- --watch=false`

Beklenen kırılmalar ve düzeltme kuralları (davranışı değil, DOM etkileşimini güncelle):
- `add-line-field` testid'sine tıklayan test → şu akışla değiştir: `component.draftTarget = '<koleksiyonAdı>'` set et (veya `selected-collection` select'inde koleksiyonu seç), sonra `einvoice-add-rule` butonuna tıkla.
- Adım 1 dışındaki DOM'u arayan testler (örn. ağaç düğümü tıklaması) → önce `component.loadSampleXml(...)` çağır (otomatik adım 2) veya `component.setStep(2)` + `fixture.detectChanges()`.
- `einvoice-draft-preview` / doğrulama testid'lerini DOM'da arayan testler → önce `component.setStep(3)` + `fixture.detectChanges()`.
- Sihirbaz/gelişmiş alan DOM testleri → `<details>` içeriği DOM'da mevcuttur (kapalı olsa da), değişiklik gerekmez.

Expected: tüm Studio testleri PASS.

- [ ] **Step 9: Görsel doğrulama**

Studio'yu başlat, `/einvoice-addressing` ekranını aç; üç adımın akışını, mobil genişlikte (≤900px) tek sütuna düşüşü ve klavye odak halkalarının görünürlüğünü kontrol et. Ekran görüntüsü alıp yerleşimi gözden geçir (dev ortamında tarayıcı aracın varsa).

- [ ] **Step 10: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.ts src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.html src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.scss src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.spec.ts src/RPA.Studio/src/app/studio/projects/einvoice-profiles/einvoice-profiles.component.html
git commit -m "refactor(studio): adresleme editoru uc adimli akisa sadelestirildi

Ornek XML -> Alanlari bagla -> Dogrula adimlari; dort yan yana panel
yerine agac + tek alan karti; regex/fallback 'Gelismis' altina indi;
koleksiyon ayri panel yerine 'Nereye eklensin?' hedef secimine tasindi;
JSON textarea'lar details icine alindi.

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 10: Tam regresyon + doğrulama

**Files:** yok (yalnız doğrulama).

**Interfaces:**
- Consumes: tüm önceki görevler.
- Produces: yeşil test matrisi.

- [ ] **Step 1: Backend tam test**

Run: `dotnet test tests/RPA.Domain.Tests` ; `dotnet test tests/RPA.Application.Tests` ; `dotnet test tests/RPA.Infrastructure.Tests` ; `dotnet test tests/RPA.WebAPI.Tests`
Expected: hepsi PASS.

- [ ] **Step 2: Studio tam test**

Run: `cd src/RPA.Studio` sonra `npm test -- --watch=false`
Expected: PASS.

- [ ] **Step 3: Uçtan uca senaryo doğrulaması (manuel veya superpowers:verification-before-completion)**

Studio'yu başlat (`.claude/launch.json` varsa oradaki config ile), `/einvoice-addressing` ekranında:
1. Profil oluştur, örnek UBL XML yükle.
2. Ağaçtan `cbc:ID` seç → alan ekle → Doğrulama panelinde değerin **yeşil** ve "XPath" rozetiyle göründüğünü gör.
3. Olmayan bir XPath yaz + "Fallback yardımcısı" ile IBAN preset'i uygula → değerin "Regex fallback" rozetiyle bulunduğunu gör.
4. `satirlar` koleksiyonu + ağaçtan satır alanı ekle → alan yolunun `./...` göreceli kaydedildiğini ve tabloda satır değerlerinin farklı olduğunu gör.
5. Yayınla; designer'da `EInvoice.ReadProfile` node'una profili bağla → değişken kataloğunda `fatura.*` alanlarını gör.
6. Profili tekrar yayınla (v2) → designer'da node'u aç → "daha yeni sürüm" uyarısını ve "Son sürüme geç" davranışını doğrula.

- [ ] **Step 4: Branch durumunu raporla**

`git log --oneline feat/studio-login-dashboard-activities..HEAD` çıktısını kullanıcıya özetle; merge/PR kararını kullanıcıya bırak (superpowers:finishing-a-development-branch).

---

## Self-Review Notları (plan yazarı tarafından yapıldı)

- **Spec kapsaması:** Analizdeki öneriler → Task eşlemesi: fallback zinciri (T2+T4), TR format (T3+T4), göreceli XPath düzeltmesi (T5), görsel doğrulama (T6), regex sihirbazı (T7), sürüm uyarısı (T8), yerleşim sadeleştirmesi / adımlı akış (T9), `[Authorize]` (T1). Örnek-XML kalıcılığı bilinçli kapsam dışı (spec'te "yalnız tarayıcı belleğinde" kararı korunur; T9'daki boş-durum mesajları yönlendirme sağlar).
- **Görev sırası bağımlılığı:** T9, T6 ve T7'nin HTML çıktısını nihai adımlı yerleşime taşır — T6/T7 tamamlanmadan T9'a başlanmaz. T9 Step 5'teki HTML, T6+T7 parçalarının birleşmiş NİHAİ halidir; çakışma çözmek yerine dosyanın tamamı bu içerikle değiştirilir.
- **Tip tutarlılığı:** `fallbackRegex`/`fallbackGroup` adları backend (`FallbackRegex` → JSON camelCase, `PropertyNameCaseInsensitive=true`) ve Studio arayüzünde birebir. `matchedBy` yalnız Studio önizlemesinde yaşar; runtime çıktısına yazılmaz (workflow nesnesini kirletmemek bilinçli karar).
- **Mevcut test uyumu:** Extractor'da `yyyy-MM-dd` ilk formattır; `Convert` string/integer/boolean kolları değişmedi. Studio `convert` date kolu ISO string döndürmeye devam eder.
- **Bilinen sınırlar (YAGNI ertelemeleri):** Fallback yalnız tek regex'tir (N-adımlı strateji zinciri değil) — kullanıcı akışı "XPath, olmazsa regex" olduğundan yeterli. Sihirbazın "seçimden üret"i tek satırlık çapa üretir; çok satırlı çapa gerekirse ayrı iş.

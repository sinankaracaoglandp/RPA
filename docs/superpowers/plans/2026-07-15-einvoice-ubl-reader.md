# E-Fatura UBL Okuyucu Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** UBL-TR XML faturalarını güvenli biçimde okuyup standart/özel alanları workflow değişkenlerine çıkaran tekli ve batch aktiviteleri ile üç panelli XPath/regex Studio editörünü oluşturmak.

**Architecture:** `UblInvoiceParser` güvenli XML yükleme, namespace çözme, standart UBL alanları ve özel XPath/regex kurallarının tek kaynağıdır. `EInvoice.ReadUbl` ile `EInvoice.ReadUblBatch` aynı parser'ı kullanır; Angular editörü aynı mapping JSON sözleşmesini üretir ve örnek XML'i yalnızca tarayıcı belleğinde tutar.

**Tech Stack:** .NET 10, `System.Xml`, LINQ to XML, timeout'lu `System.Text.RegularExpressions`, xUnit, Angular standalone components, Vitest/Karma Angular test runner.

## Global Constraints

- TDD sırası her görevde failing test → minimal implementation → pass → commit olmalıdır.
- DTD ve dış entity çözümleme kapalı olmalıdır; örnek veya çalışma zamanı XML içeriği loglanmamalıdır.
- `filePath` ile `xmlContent`, batch'te `filePaths` ile `xmlContents`, karşılıklı dışlayıcıdır.
- Regex timeout varsayılanı 500 ms olmalı; XML azami karakter sayısı varsayılan 10 MiB olmalıdır.
- Örnek XML ve örnek dosya yolu workflow JSON'una kaydedilmemelidir.
- Mevcut `IActivity` ve `IWorkflowRunner` public imzaları değiştirilmeyecektir.
- Şema değişikliği yapılmadan önce AGENTS.md kontrat değişikliği kaydı eklenecektir.
- Canvas/motor teslimi sonunda `/code-review high` karşılığı yüksek eforlu review yapılmalıdır.

---

## File Structure

- Create `src/RPA.Infrastructure/Workflow/Activities/EInvoice/EInvoiceModels.cs`: parser giriş/çıkışları, mapping ve batch sonuç tipleri.
- Create `src/RPA.Infrastructure/Workflow/Activities/EInvoice/UblInvoiceParser.cs`: güvenli XML yükleme ve standart/özel alan çıkarma.
- Create `src/RPA.Infrastructure/Workflow/Activities/EInvoice/EInvoiceActivities.cs`: tekli/batch `IActivity` adaptörleri.
- Create `tests/RPA.Infrastructure.Tests/Workflow/EInvoice/UblInvoiceParserTests.cs`: parser güvenlik ve alan testleri.
- Create `tests/RPA.Infrastructure.Tests/Workflow/EInvoice/EInvoiceActivityTests.cs`: aktivite kaynak/çıktı/batch testleri.
- Modify `src/RPA.Infrastructure/Workflow/ActivityRegistry.cs`: katalog metadata'sı.
- Modify `src/RPA.Infrastructure/Workflow/WorkflowServiceCollectionExtensions.cs`: keyed activity DI kayıtları.
- Modify `src/RPA.Domain/WorkflowSchema.json`: yeni aktivite parametre/çıktı kontratı.
- Modify `AGENTS.md`: 2026-07-15 e-fatura aktivite kontrat kaydı.
- Modify `src/RPA.Studio/src/app/shared/models/activity.model.ts`: `einvoice-mapping` picker türü.
- Create `src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping.model.ts`: editörün mapping tipleri.
- Create `src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.{ts,html,scss,spec.ts}`: üç panelli editör.
- Modify `src/RPA.Studio/src/app/studio/designer/properties/generic-property.component.{ts,html,spec.ts}`: picker routing ve workflow property yazımı.

---

### Task 1: Kontrat Kaydı ve E-Fatura Veri Modelleri

**Files:**
- Modify: `AGENTS.md`
- Create: `src/RPA.Infrastructure/Workflow/Activities/EInvoice/EInvoiceModels.cs`
- Test: `tests/RPA.Infrastructure.Tests/Workflow/EInvoice/UblInvoiceParserTests.cs`

**Interfaces:**
- Consumes: `System.Xml.Linq.XDocument`; mevcut public Domain arayüzünü değiştirmez.
- Produces: `InvoiceData`, `InvoiceLineData`, `InvoicePartyData`, `InvoiceTaxData`, `InvoiceMappingRule`, `InvoiceBatchItemResult`, `InvoiceParseOptions`, `InvoiceParseException`.

- [ ] **Step 1: Model sözleşmesini derleme testiyle sabitle**

```csharp
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
```

- [ ] **Step 2: Testi çalıştır ve model tipleri bulunamadığı için başarısız olduğunu doğrula**

Run: `dotnet test tests/RPA.Infrastructure.Tests --no-restore --filter "FullyQualifiedName~UblInvoiceParserTests.Models_ExposeStableWorkflowShape"`

Expected: FAIL; `InvoiceData` ve ilişkili tipler tanımlı değildir.

- [ ] **Step 3: Modelleri ve kesin varsayılanları ekle**

```csharp
namespace RPA.Infrastructure.Workflow.Activities.EInvoice;

public sealed class InvoiceData
{
    public string? Uuid { get; init; }
    public string? InvoiceNumber { get; init; }
    public DateOnly? IssueDate { get; init; }
    public string? InvoiceType { get; init; }
    public string? ProfileId { get; init; }
    public string? Currency { get; init; }
    public InvoicePartyData? Supplier { get; init; }
    public InvoicePartyData? Customer { get; init; }
    public List<InvoiceLineData> Lines { get; init; } = [];
    public List<string> Notes { get; init; } = [];
    public decimal? ExchangeRate { get; set; }
    public List<string> PaymentAccounts { get; init; } = [];
    public decimal? TaxExclusiveAmount { get; init; }
    public decimal? TaxInclusiveAmount { get; init; }
    public decimal? PayableAmount { get; init; }
    public Dictionary<string, object?> CustomFields { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class InvoicePartyData { public string? Name { get; init; } public string? TaxId { get; init; } public string? TaxOffice { get; init; } }
public sealed class InvoiceLineData { public string? Id { get; init; } public string? ItemCode { get; init; } public string? Name { get; init; } public decimal? Quantity { get; init; } public string? UnitCode { get; init; } public decimal? UnitPrice { get; init; } public decimal? LineExtensionAmount { get; init; } public List<string> Notes { get; init; } = []; }
public sealed record InvoiceTaxData(string? Code, string? Name, decimal? Percent, decimal? Amount);
public sealed record InvoiceMappingRule(string Name, string Source, string? ScopeXPath, string? ValueXPath, string? Regex, string? Group, string Type = "string", bool Required = false, bool Multiple = false);
public sealed record InvoiceBatchItemResult(int SourceIndex, bool Success, InvoiceData? Invoice, string? Error);
public sealed record InvoiceParseOptions(int MaxCharacters = 10 * 1024 * 1024, TimeSpan? RegexTimeout = null) { public TimeSpan EffectiveRegexTimeout => RegexTimeout ?? TimeSpan.FromMilliseconds(500); }
public sealed class InvoiceParseException(string message) : Exception(message);
```

AGENTS.md'ye `## Kontrat Değişikliği — 2026-07-15 (E-Invoice UBL Activities)` başlığı altında yeni aktivite kimlikleri, parametreleri, etkilenmiş Domain şema/Infrastructure/Studio paketleri ve gerekçeyi ekle.

- [ ] **Step 4: Model testini çalıştır ve geçtiğini doğrula**

Run: `dotnet test tests/RPA.Infrastructure.Tests --no-restore --filter "FullyQualifiedName~UblInvoiceParserTests.Models_ExposeStableWorkflowShape"`

Expected: PASS, 1 test.

- [ ] **Step 5: Commit**

```bash
git add AGENTS.md src/RPA.Infrastructure/Workflow/Activities/EInvoice/EInvoiceModels.cs tests/RPA.Infrastructure.Tests/Workflow/EInvoice/UblInvoiceParserTests.cs
git commit -m "feat(einvoice): UBL veri modeli kontrati" -m "Co-Authored-By: Codex Opus <noreply@anthropic.com>"
```

### Task 2: Güvenli XML Yükleme ve Standart UBL Alanları

**Files:**
- Create: `src/RPA.Infrastructure/Workflow/Activities/EInvoice/UblInvoiceParser.cs`
- Modify: `tests/RPA.Infrastructure.Tests/Workflow/EInvoice/UblInvoiceParserTests.cs`

**Interfaces:**
- Consumes: `InvoiceParseOptions`.
- Produces: `InvoiceData Parse(string xml, IReadOnlyList<InvoiceMappingRule>? mappings = null)` and `InvoiceData ParseFile(string filePath, IReadOnlyList<InvoiceMappingRule>? mappings = null)`.

- [ ] **Step 1: Namespace'li UBL ve XXE testlerini yaz**

```csharp
[Fact]
public void Parse_ReadsNamespacedHeaderPartiesTotalsAndLines()
{
    var invoice = new UblInvoiceParser().Parse(SampleUbl.Xml);
    Assert.Equal("FTR202600001", invoice.InvoiceNumber);
    Assert.Equal(new DateOnly(2026, 7, 15), invoice.IssueDate);
    Assert.Equal("TRY", invoice.Currency);
    Assert.Equal("Satıcı AŞ", invoice.Supplier!.Name);
    Assert.Equal("1234567890", invoice.Supplier.TaxId);
    Assert.Equal(120m, invoice.PayableAmount);
    Assert.Equal("Ürün A", Assert.Single(invoice.Lines).Name);
}

[Fact]
public void Parse_RejectsDtdAndExternalEntities()
{
    const string xml = "<!DOCTYPE x [<!ENTITY ext SYSTEM 'file:///c:/windows/win.ini'>]><Invoice>&ext;</Invoice>";
    Assert.Throws<InvoiceParseException>(() => new UblInvoiceParser().Parse(xml));
}
```

- [ ] **Step 2: Testlerin parser bulunmadığı için başarısız olduğunu doğrula**

Run: `dotnet test tests/RPA.Infrastructure.Tests --no-restore --filter "FullyQualifiedName~UblInvoiceParserTests.Parse_"`

Expected: FAIL; `UblInvoiceParser` tanımlı değildir.

- [ ] **Step 3: Güvenli okuyucu ve standart alan çıkarımını uygula**

```csharp
public sealed class UblInvoiceParser(InvoiceParseOptions? options = null)
{
    private readonly InvoiceParseOptions _options = options ?? new();

    public InvoiceData Parse(string xml, IReadOnlyList<InvoiceMappingRule>? mappings = null)
    {
        if (string.IsNullOrWhiteSpace(xml) || xml.Length > _options.MaxCharacters)
            throw new InvoiceParseException("XML boş veya izin verilen boyutu aşıyor.");
        try
        {
            using var sr = new StringReader(xml);
            using var xr = XmlReader.Create(sr, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = _options.MaxCharacters });
            var doc = XDocument.Load(xr, LoadOptions.None);
            return ReadStandardFields(doc, mappings ?? []);
        }
        catch (XmlException ex) { throw new InvoiceParseException($"Geçersiz veya güvensiz XML: {ex.Message}"); }
    }

    public InvoiceData ParseFile(string filePath, IReadOnlyList<InvoiceMappingRule>? mappings = null) => Parse(File.ReadAllText(filePath), mappings);
}
```

`ReadStandardFields` içinde namespace URI'lerini kök belgeden al; `cbc` ve `cac` için prefix metnine güvenme. `ID`, `UUID`, `IssueDate`, `InvoiceTypeCode`, `ProfileID`, `DocumentCurrencyCode`, supplier/customer party, `LegalMonetaryTotal` ve her `InvoiceLine` alanını local namespace ile oku.

- [ ] **Step 4: Parser testlerini çalıştır ve geçir**

Run: `dotnet test tests/RPA.Infrastructure.Tests --no-restore --filter "FullyQualifiedName~UblInvoiceParserTests.Parse_"`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Infrastructure/Workflow/Activities/EInvoice/UblInvoiceParser.cs tests/RPA.Infrastructure.Tests/Workflow/EInvoice/UblInvoiceParserTests.cs
git commit -m "feat(einvoice): guvenli UBL parser ve standart alanlar" -m "Co-Authored-By: Codex Opus <noreply@anthropic.com>"
```

### Task 3: XPath, Regex, Not, Kur ve IBAN Eşlemeleri

**Files:**
- Modify: `src/RPA.Infrastructure/Workflow/Activities/EInvoice/UblInvoiceParser.cs`
- Modify: `tests/RPA.Infrastructure.Tests/Workflow/EInvoice/UblInvoiceParserTests.cs`

**Interfaces:**
- Consumes: `IReadOnlyList<InvoiceMappingRule>`.
- Produces: `InvoiceData.CustomFields`, `Notes`, `ExchangeRate`, `PaymentAccounts`.

- [ ] **Step 1: Özel mapping, note fallback ve timeout testlerini yaz**

```csharp
[Fact]
public void Parse_AppliesNamedRegexGroupToEveryNote()
{
    var rules = new[] { new InvoiceMappingRule("orderNumber", "InvoiceNotes", null, null, @"Sipariş No:\s*(?<value>\S+)", "value") };
    var invoice = new UblInvoiceParser().Parse(SampleUbl.WithNotes("Sipariş No: S-42", "IBAN: TR120006200012345678901234", "1 USD = 32,4567 TL"), rules);
    Assert.Equal("S-42", invoice.CustomFields["orderNumber"]);
    Assert.Equal(32.4567m, invoice.ExchangeRate);
    Assert.Equal("TR120006200012345678901234", Assert.Single(invoice.PaymentAccounts));
}

[Fact]
public void Parse_RequiredMappingWithoutMatchThrowsNamedError()
{
    var rule = new InvoiceMappingRule("requiredCode", "XPath", null, "//cbc:Note", "YOK:(?<value>.+)", "value", Required: true);
    var ex = Assert.Throws<InvoiceParseException>(() => new UblInvoiceParser().Parse(SampleUbl.Xml, [rule]));
    Assert.Contains("requiredCode", ex.Message);
}
```

- [ ] **Step 2: Yeni testlerin eşleme uygulanmadığı için başarısız olduğunu doğrula**

Run: `dotnet test tests/RPA.Infrastructure.Tests --no-restore --filter "FullyQualifiedName~UblInvoiceParserTests.Parse_Applies|FullyQualifiedName~UblInvoiceParserTests.Parse_Required"`

Expected: FAIL; custom field yoktur.

- [ ] **Step 3: Namespace manager, XPath scope ve timeout'lu regex uygula**

```csharp
private object? ApplyRule(XDocument doc, InvoiceMappingRule rule)
{
    var ns = CreateNamespaceManager(doc);
    var scopes = string.IsNullOrWhiteSpace(rule.ScopeXPath)
        ? new XPathNavigator?[] { doc.CreateNavigator() }
        : Select(doc, rule.ScopeXPath!, ns).Select(e => e.CreateNavigator()).ToArray();
    var values = scopes.SelectMany(s => ReadRuleValues(s!, rule, ns)).ToList();
    if (values.Count == 0 && rule.Required) throw new InvoiceParseException($"Zorunlu eşleme bulunamadı: {rule.Name}");
    return rule.Multiple ? values : values.FirstOrDefault();
}

private string? Match(string value, InvoiceMappingRule rule)
{
    if (string.IsNullOrWhiteSpace(rule.Regex)) return value;
    var match = Regex.Match(value, rule.Regex, RegexOptions.CultureInvariant, _options.EffectiveRegexTimeout);
    if (!match.Success) return null;
    var group = string.IsNullOrWhiteSpace(rule.Group) ? match.Groups[0] : match.Groups[rule.Group];
    return group.Success ? group.Value : null;
}
```

`InvoiceNotes` ve `LineNotes` kaynaklarını XPath'ten ayrı ele al. Standart `PricingExchangeRate/CalculationRate` ve `PayeeFinancialAccount/ID` değerlerine öncelik ver; yoksa düzenlenebilir varsayılan regex'lerle notlarda kur ve IBAN ara. `ConvertValue` string/decimal/integer/date/boolean dönüşümlerini deterministik yap.

- [ ] **Step 4: Tüm parser testlerini çalıştır ve geçir**

Run: `dotnet test tests/RPA.Infrastructure.Tests --no-restore --filter "FullyQualifiedName~UblInvoiceParserTests"`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Infrastructure/Workflow/Activities/EInvoice/UblInvoiceParser.cs tests/RPA.Infrastructure.Tests/Workflow/EInvoice/UblInvoiceParserTests.cs
git commit -m "feat(einvoice): XPath regex ve not alan esleme" -m "Co-Authored-By: Codex Opus <noreply@anthropic.com>"
```

### Task 4: Tekli ve Batch Workflow Aktiviteleri

**Files:**
- Create: `src/RPA.Infrastructure/Workflow/Activities/EInvoice/EInvoiceActivities.cs`
- Create: `tests/RPA.Infrastructure.Tests/Workflow/EInvoice/EInvoiceActivityTests.cs`

**Interfaces:**
- Consumes: `UblInvoiceParser.Parse`, `ParseFile`, `IActivityExecutionContext`.
- Produces: keyed activities `EInvoice.ReadUbl`, `EInvoice.ReadUblBatch`; outputs `invoice`, `lines`, `customFields`, `results`.

- [ ] **Step 1: Kaynak dışlama, output binding ve batch policy testlerini yaz**

```csharp
[Fact]
public async Task ReadUbl_XmlContent_SetsStableOutputsAndNamedBindings()
{
    var ctx = FakeActivityContext.With(("xmlContent", SampleUbl.Xml), ("outputBindings", "{\"invoiceNumber\":\"faturaNo\"}"));
    await new ReadUblActivity(new UblInvoiceParser()).ExecuteAsync(ctx);
    Assert.Equal("FTR202600001", ctx.Variables["faturaNo"]);
    Assert.IsType<InvoiceData>(ctx.Variables["invoice"]);
    Assert.IsType<List<InvoiceLineData>>(ctx.Variables["lines"]);
}

[Fact]
public async Task ReadUbl_BothSources_ThrowsValidationError()
{
    var ctx = FakeActivityContext.With(("filePath", "a.xml"), ("xmlContent", "<Invoice />"));
    await Assert.ThrowsAsync<InvoiceParseException>(() => new ReadUblActivity(new UblInvoiceParser()).ExecuteAsync(ctx));
}

[Fact]
public async Task Batch_Continue_ReturnsSuccessAndFailureItems()
{
    var ctx = FakeActivityContext.With(("xmlContents", new[] { SampleUbl.Xml, "<broken" }), ("errorMode", "Continue"));
    await new ReadUblBatchActivity(new UblInvoiceParser()).ExecuteAsync(ctx);
    var results = Assert.IsType<List<InvoiceBatchItemResult>>(ctx.Variables["results"]);
    Assert.True(results[0].Success); Assert.False(results[1].Success);
}
```

- [ ] **Step 2: Aktivite tipleri bulunmadığı için testlerin başarısız olduğunu doğrula**

Run: `dotnet test tests/RPA.Infrastructure.Tests --no-restore --filter "FullyQualifiedName~EInvoiceActivityTests"`

Expected: FAIL.

- [ ] **Step 3: Aktiviteleri minimal orkestrasyonla uygula**

```csharp
public sealed class ReadUblActivity(UblInvoiceParser parser) : IActivity
{
    public async Task ExecuteAsync(IActivityExecutionContext context)
    {
        var path = context.GetVariable<string?>("filePath");
        var xml = context.GetVariable<string?>("xmlContent");
        if (string.IsNullOrWhiteSpace(path) == string.IsNullOrWhiteSpace(xml))
            throw new InvoiceParseException("filePath veya xmlContent alanlarından tam olarak biri sağlanmalıdır.");
        var mappings = EInvoiceJson.ReadMappings(context.GetVariable<object?>("mappings"));
        var invoice = xml is not null ? parser.Parse(xml, mappings) : parser.ParseFile(path!, mappings);
        context.SetVariable("invoice", invoice);
        context.SetVariable("lines", invoice.Lines);
        context.SetVariable("customFields", invoice.CustomFields);
        EInvoiceJson.ApplyOutputBindings(context, invoice, context.GetVariable<object?>("outputBindings"));
        await Task.CompletedTask;
    }
}
```

Batch aktivitesinde kaynak koleksiyonunu normalize et, indeks sırasını koru, `Continue` için güvenli hata metniyle sonuç ekle ve `Stop` için `InvoiceParseException` yükselt. Tam XML'i hata metnine ekleme.

- [ ] **Step 4: Aktivite testlerini çalıştır ve geçir**

Run: `dotnet test tests/RPA.Infrastructure.Tests --no-restore --filter "FullyQualifiedName~EInvoiceActivityTests"`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Infrastructure/Workflow/Activities/EInvoice/EInvoiceActivities.cs tests/RPA.Infrastructure.Tests/Workflow/EInvoice/EInvoiceActivityTests.cs
git commit -m "feat(einvoice): tekli ve batch workflow aktiviteleri" -m "Co-Authored-By: Codex Opus <noreply@anthropic.com>"
```

### Task 5: Katalog, DI ve Workflow Şeması

**Files:**
- Modify: `src/RPA.Infrastructure/Workflow/ActivityRegistry.cs`
- Modify: `src/RPA.Infrastructure/Workflow/WorkflowServiceCollectionExtensions.cs`
- Modify: `src/RPA.Domain/WorkflowSchema.json`
- Modify: `tests/RPA.Infrastructure.Tests/Workflow/ActivityRegistryCoverageTests.cs`
- Modify: `tests/RPA.Infrastructure.Tests/Workflow/WorkflowSchemaValidationTests.cs`

**Interfaces:**
- Consumes: Task 4 aktiviteleri.
- Produces: katalog ve runtime factory erişimi; `pickerKind: "einvoice-mapping"` metadata'sı.

- [ ] **Step 1: Katalog ve şema testlerini önce yaz**

```csharp
[Fact]
public void Catalog_ContainsEInvoiceActivitiesWithMappingPicker()
{
    var catalog = ActivityRegistry.BuildCatalog();
    var single = catalog["EInvoice.ReadUbl"];
    Assert.Equal("einvoice-mapping", single.Inputs.Single(x => x.Name == "mappings").PickerKind);
    Assert.Contains(single.Outputs, x => x.Name == "invoice");
    Assert.True(catalog.ContainsKey("EInvoice.ReadUblBatch"));
}
```

Şema testine iki kaynağın birlikte reddedildiği, tekli ve batch geçerli örnekleri ekle.

- [ ] **Step 2: Testleri çalıştır ve kayıtlar olmadığı için başarısızlığı doğrula**

Run: `dotnet test tests/RPA.Infrastructure.Tests --no-restore --filter "FullyQualifiedName~ActivityRegistryCoverageTests|FullyQualifiedName~WorkflowSchemaValidationTests"`

Expected: FAIL; aktivite veya şema tanımı yoktur.

- [ ] **Step 3: Katalog, keyed DI ve JSON Schema tanımlarını ekle**

```csharp
b.Activity("EInvoice.ReadUbl")
 .DisplayName("E-Fatura UBL Oku").Category("E-Fatura")
 .Input("filePath", "string", required: false)
 .Input("xmlContent", "string", required: false)
 .Input("mappings", "JSON", required: false, pickerKind: "einvoice-mapping")
 .Input("outputBindings", "JSON", required: false)
 .Output("invoice", "JSON").Output("lines", "List<JSON>").Output("customFields", "JSON");
```

Batch için `filePaths`, `xmlContents`, `errorMode`, `mappings`, `outputBindings`, `results` ekle. DI'da aynı singleton `UblInvoiceParser` üzerinden iki keyed `IActivity` kaydet. WorkflowSchema'da aktivite enum/koşullu parametre tanımlarını ekle; tekli ve batch kaynak çiftlerini `oneOf` ile karşılıklı dışla.

- [ ] **Step 4: Katalog/şema testlerini çalıştır ve geçir**

Run: `dotnet test tests/RPA.Infrastructure.Tests --no-restore --filter "FullyQualifiedName~ActivityRegistryCoverageTests|FullyQualifiedName~WorkflowSchemaValidationTests"`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Infrastructure/Workflow/ActivityRegistry.cs src/RPA.Infrastructure/Workflow/WorkflowServiceCollectionExtensions.cs src/RPA.Domain/WorkflowSchema.json tests/RPA.Infrastructure.Tests/Workflow/ActivityRegistryCoverageTests.cs tests/RPA.Infrastructure.Tests/Workflow/WorkflowSchemaValidationTests.cs
git commit -m "feat(contract): e-fatura aktiviteleri katalog ve sema" -m "Kontrat Degisikligi AGENTS.md dosyasinda belirtilmistir." -m "Co-Authored-By: Codex Opus <noreply@anthropic.com>"
```

### Task 6: Studio Mapping Modeli ve XML Ağaç Motoru

**Files:**
- Create: `src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping.model.ts`
- Create: `src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.ts`
- Create: `src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.spec.ts`
- Modify: `src/RPA.Studio/src/app/shared/models/activity.model.ts`

**Interfaces:**
- Consumes: Task 1 `InvoiceMappingRule` JSON şekli.
- Produces: `EInvoiceMappingRule`, `XmlTreeNode`, `parseSampleXml`, `buildXPath`, `previewRule`, `valueChange` output'u.

- [ ] **Step 1: XML ağacı, namespace XPath ve regex preview testlerini yaz**

```ts
it('builds namespace-aware xpath and named regex groups', () => {
  const component = new EInvoiceMappingEditorComponent();
  component.loadSampleXml(SAMPLE_UBL);
  const id = component.findFirst('cbc:ID')!;
  expect(component.buildXPath(id)).toBe('/Invoice/cbc:ID');
  const preview = component.preview({
    name: 'year', source: 'XPath', valueXPath: '/Invoice/cbc:ID',
    regex: '^FTR(?<value>\\d{4})', group: 'value', type: 'string', required: false, multiple: false
  });
  expect(preview.converted).toBe('2026');
});

it('never emits sample xml in mapping value', () => {
  const component = new EInvoiceMappingEditorComponent();
  component.loadSampleXml(SAMPLE_UBL);
  component.addRule(MAPPING);
  expect(JSON.stringify(component.serializedValue())).not.toContain('<Invoice');
});
```

- [ ] **Step 2: Testleri çalıştır ve component bulunmadığı için başarısızlığı doğrula**

Run: `npm test -- --watch=false "--include=**/einvoice-mapping-editor.component.spec.ts"`

Expected: FAIL.

- [ ] **Step 3: Tipleri ve salt bellek örnek parser'ını uygula**

```ts
export interface EInvoiceMappingRule {
  name: string; source: 'Standard'|'XPath'|'InvoiceNotes'|'LineNotes';
  scopeXPath?: string|null; valueXPath?: string|null; regex?: string|null;
  group?: string|null; type: 'string'|'decimal'|'integer'|'date'|'boolean';
  required: boolean; multiple: boolean;
}

loadSampleXml(xml: string): void {
  const doc = new DOMParser().parseFromString(xml, 'application/xml');
  if (doc.querySelector('parsererror')) throw new Error('Geçersiz XML örneği.');
  this.sampleDocument = doc;       // hiçbir valueChange payload'una eklenmez
  this.tree = [this.toTree(doc.documentElement, undefined)];
}
```

XPath değerlendirmesinde `document.evaluate` ve namespace resolver kullan. Regex'i `new RegExp` ile çalıştır, grup/converted/error alanlarını döndür. Hazır kur ve IBAN kuralları statik factory fonksiyonları olsun.

- [ ] **Step 4: Component mantık testlerini çalıştır ve geçir**

Run: `npm test -- --watch=false "--include=**/einvoice-mapping-editor.component.spec.ts"`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Studio/src/app/shared/models/activity.model.ts src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping.model.ts src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.ts src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.spec.ts
git commit -m "feat(studio): XML XPath regex editor motoru" -m "Co-Authored-By: Codex Opus <noreply@anthropic.com>"
```

### Task 7: Üç Panelli Studio UI ve Generic Property Entegrasyonu

**Files:**
- Create: `src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.html`
- Create: `src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.scss`
- Modify: `src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.spec.ts`
- Modify: `src/RPA.Studio/src/app/studio/designer/properties/generic-property.component.ts`
- Modify: `src/RPA.Studio/src/app/studio/designer/properties/generic-property.component.html`
- Modify: `src/RPA.Studio/src/app/studio/designer/properties/generic-property.component.spec.ts`

**Interfaces:**
- Consumes: `pickerKind === 'einvoice-mapping'`, Task 6 component API.
- Produces: dosya seçimi, üç panel, rule JSON `valueChange`; örnek dosya state'i dışarı çıkmaz.

- [ ] **Step 1: Görsel davranış ve routing testlerini yaz**

```ts
it('renders tree rule and preview panels and routes mapping picker', async () => {
  host.port = { name: 'mappings', type: 'JSON', pickerKind: 'einvoice-mapping' };
  fixture.detectChanges();
  expect(fixture.nativeElement.querySelector('[data-testid="einvoice-tree-panel"]')).toBeTruthy();
  expect(fixture.nativeElement.querySelector('[data-testid="einvoice-rule-panel"]')).toBeTruthy();
  expect(fixture.nativeElement.querySelector('[data-testid="einvoice-preview-panel"]')).toBeTruthy();
});
```

- [ ] **Step 2: UI testlerini çalıştır ve şablon/routing olmadığı için başarısızlığı doğrula**

Run: `npm test -- --watch=false "--include=**/{einvoice-mapping-editor,generic-property}.component.spec.ts"`

Expected: FAIL.

- [ ] **Step 3: Üç paneli ve generic picker routing'i uygula**

```html
@if (isEInvoiceMapping(port)) {
  <app-einvoice-mapping-editor
    [value]="stringValue(port)"
    (valueChange)="onValueChange(port, $event)" />
} @else {
  <!-- mevcut generic alan dalları değişmeden devam eder -->
}
```

Editör template'inde `<input type="file" accept=".xml,text/xml,application/xml">`, erişilebilir tree butonları, rule formu ve preview JSON kullan. SCSS'te masaüstünde `grid-template-columns: 30% 38% 32%`; dar panelde tek sütun yap. Dosyayı `File.text()` ile oku ve file nesnesini component dışına emit etme.

- [ ] **Step 4: Studio odaklı testleri ve build'i çalıştır**

Run: `npm test -- --watch=false "--include=**/{einvoice-mapping-editor,generic-property}.component.spec.ts"`

Expected: PASS.

Run: `npm run build`

Expected: exit 0; yalnız mevcut bütçe/CommonJS uyarıları kabul edilir.

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.html src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.scss src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.spec.ts src/RPA.Studio/src/app/studio/designer/properties/generic-property.component.ts src/RPA.Studio/src/app/studio/designer/properties/generic-property.component.html src/RPA.Studio/src/app/studio/designer/properties/generic-property.component.spec.ts
git commit -m "feat(studio): uc panelli e-fatura esleme editoru" -m "Co-Authored-By: Codex Opus <noreply@anthropic.com>"
```

### Task 8: Uçtan Uca Workflow Uyumluluğu ve Teslim Doğrulaması

**Files:**
- Modify: `tests/RPA.Infrastructure.Tests/BaseRunnerTests.cs`
- Modify: `tests/RPA.Infrastructure.Tests/Workflow/EInvoice/EInvoiceActivityTests.cs`
- Modify: `docs/superpowers/specs/2026-07-15-einvoice-ubl-reader-design.md` only if implementation reveals an approved clarification; do not silently change behavior.

**Interfaces:**
- Consumes: katalog, aktiviteler, BaseRunner output mapping ve `Logic.ForEach`.
- Produces: kanıtlanmış `ReadUbl → workflow variable → ForEach/downstream activity` akışı.

- [ ] **Step 1: Runner entegrasyon testini yaz**

```csharp
[Fact]
public async Task Runner_ReadUblOutputsFeedForEachAndDownstreamActivity()
{
    var workflow = WorkflowFixture.ReadUblThenForEach(SampleUbl.WithTwoLines());
    var result = await CreateRunnerWithEInvoiceActivities().ExecuteAsync(workflow, new Dictionary<string, object?>(), Guid.NewGuid());
    Assert.True(result.Success);
    Assert.Equal(new[] { "URUN-1", "URUN-2" }, RecordingActivity.SeenValues);
}
```

- [ ] **Step 2: Entegrasyon testinin eksik fixture/bağlama nedeniyle önce başarısız olduğunu doğrula**

Run: `dotnet test tests/RPA.Infrastructure.Tests --no-restore --filter "FullyQualifiedName~Runner_ReadUblOutputsFeedForEachAndDownstreamActivity"`

Expected: FAIL before fixture/registration completion.

- [ ] **Step 3: Sadece testin gerektirdiği fixture ve output bağlamasını tamamla**

Workflow JSON'da `EInvoice.ReadUbl` node'unun `lines` çıktısını `invoiceLines` değişkenine, `Logic.ForEach.items` girdisini `{{invoiceLines}}`, `itemVariable` değerini `line` olarak bağla. Downstream recording activity ürün kodunu mevcut expression/variable sistemiyle alsın; yeni runner semantiği ekleme.

- [ ] **Step 4: Tüm odaklı doğrulamaları çalıştır**

Run: `dotnet test tests/RPA.Infrastructure.Tests --no-restore --filter "FullyQualifiedName~EInvoice|FullyQualifiedName~UblInvoice|FullyQualifiedName~ActivityRegistryCoverageTests|FullyQualifiedName~WorkflowSchemaValidationTests|FullyQualifiedName~Runner_ReadUbl" -m:1 -nodeReuse:false`

Expected: tüm odaklı backend testleri PASS.

Run: `npm test -- --watch=false "--include=**/{einvoice-mapping-editor,generic-property,canvas,node}.component.spec.ts"`

Expected: tüm odaklı Studio testleri PASS.

Run: `npm run build`

Expected: exit 0.

- [ ] **Step 5: Yüksek eforlu code review uygula ve kritik/önemli bulguları düzeltip doğrulamaları yeniden çalıştır**

Review kapsamı: XXE/XML sınırları, regex ReDoS timeout'u, XPath namespace doğruluğu, hassas veri logları, output binding enjeksiyonu, batch hata izolasyonu, Studio örnek XML sızıntısı ve Canvas/ForEach uyumluluğu.

- [ ] **Step 6: Final commit**

```bash
git add tests/RPA.Infrastructure.Tests/BaseRunnerTests.cs tests/RPA.Infrastructure.Tests/Workflow/EInvoice/EInvoiceActivityTests.cs
git commit -m "test(einvoice): runner ve foreach entegrasyonu" -m "Co-Authored-By: Codex Opus <noreply@anthropic.com>"
```

## Plan Self-Review

- Spec coverage: tekli/batch kaynaklar, standart alanlar, XPath/regex, note kur/IBAN, output binding, ForEach, üç panelli editör, güvenlik ve örnek XML gizliliği Task 1-8 arasında kapsanmıştır.
- Placeholder scan: bütün uygulama adımları kesin dosya, davranış, komut ve beklenen sonuç içerir.
- Type consistency: `InvoiceMappingRule`, `InvoiceData`, `InvoiceBatchItemResult`, `invoice`, `lines`, `customFields`, `results` adları backend, katalog, Studio ve entegrasyon görevlerinde aynıdır.

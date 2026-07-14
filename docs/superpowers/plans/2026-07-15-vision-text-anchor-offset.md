# Vision Metin Çapası Ofset Tıklama — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** OCR ile bulunan bir metin çapasından `(dx, dy)` piksel ofsetle tıklayan `Vision.ClickTextOffset` aktivitesini, görsel picker'ı ve Studio editörüyle birlikte eklemek.

**Architecture:** Runtime motoru `IVisionAutomationChannel`'a tek metot ekler; çapa OCR kelime kutusunun merkezine ofset uygulanır (picker-zamanı ve runtime aynı referansı kullanır). Görsel picker `text-offset` kind'ı olarak mevcut freeze/seçim altyapısını yeniden kullanır; Studio küçük bir editör bileşeniyle `{anchorText,dx,dy}` JSON'unu düzenler.

**Tech Stack:** C# (.NET 10), xUnit + Moq, OpenCvSharp + Tesseract (Agent), Angular + Jest (Studio), SignalR (StudioHub).

## Global Constraints

- Onion mimarisi: Domain harici bağımlılık almaz; aktiviteler platform-nötr (yalnız arayüze bağlı, testte mock'lanır); Windows-only impl `RPA.Agent`'ta.
- Exception sınıflandırması: parametre doğrulama → `BusinessException`; çapa bulunamadı/timeout → `SystemException`.
- Kontrat değişikliği CLAUDE.md'ye `## Kontrat Değişikliği — 2026-07-15` başlığıyla yazılır.
- Aktivite ID nokta-notasyonu: `Vision.ClickTextOffset`; kategori "Görüntü"; capability `vision`.
- Türkçe kullanıcı-görünür metin (mevcut Vision desenine uygun).
- Test eşiği: her task sonunda ilgili `dotnet test` PASS.

---

## File Structure

- `src/RPA.Domain/Interfaces/IVisionAutomationChannel.cs` — **Modify**: `ClickTextOffsetAsync` eklenir.
- `src/RPA.Infrastructure/Activities/Vision/UnavailableVisionAutomationChannel.cs` — **Modify**: yeni metot (fırlatır).
- `src/RPA.Infrastructure/Activities/Vision/VisionActivities.cs` — **Modify**: `VisionClickTextOffsetActivity` + `TextOffsetSpec`.
- `src/RPA.Infrastructure/Workflow/WorkflowServiceCollectionExtensions.cs` — **Modify**: keyed DI.
- `src/RPA.Infrastructure/Workflow/ActivityRegistry.cs` — **Modify**: katalog girişi.
- `src/RPA.Agent/Vision/OcrEngine.cs` — **Create**: paylaşılan OCR yardımcı (kelime kutuları).
- `src/RPA.Agent/Vision/TesseractOpenCvVisionChannel.cs` — **Modify**: `ClickTextOffsetAsync` + `OcrEngine` kullanımı + `VisionOffset` yardımcı.
- `src/RPA.Infrastructure/UISpy/SapGuiElementSender.cs` — **Modify**: `SpyElementMessage.FromTextOffset` + alanlar.
- `src/RPA.Agent/UISpy/SpySessionCoordinator.cs` — **Modify**: `ITextOffsetPicker`, `TextOffsetPick`, `text-offset` dalı.
- `src/RPA.Agent/UISpy/GdiTextOffsetPicker.cs` — **Create**: iki aşamalı görsel picker.
- `src/RPA.Agent/AgentServiceCollectionExtensions.cs` — **Modify**: picker kaydı.
- `src/RPA.WebAPI/Hubs/StudioHub.cs` — **Modify**: `SupportedKinds += "text-offset"`.
- `src/RPA.Studio/src/app/shared/services/spy.service.ts` — **Modify**: `SpyKind`, `SpyElement` alanları.
- `src/RPA.Studio/src/app/studio/designer/properties/text-offset-editor.component.ts` (+`.html`,`.scss`) — **Create**.
- `src/RPA.Studio/src/app/studio/designer/properties/generic-property.component.ts` (+`.html`) — **Modify**: dispatch.
- `src/RPA.Studio/src/assets/i18n/*.json` — **Modify**: i18n.

Test dosyaları:
- `tests/RPA.Infrastructure.Tests/Activities/VisionActivitiesTests.cs`
- `tests/RPA.Infrastructure.Tests/Workflow/VisionCatalogTests.cs`
- `tests/RPA.Infrastructure.Tests/UISpy/SpyElementMessageTests.cs`
- `tests/RPA.Agent.Tests/UISpy/SpySessionCoordinatorTextOffsetTests.cs`
- `tests/RPA.Agent.Tests/Vision/VisionOffsetTests.cs` (**Create**)
- `src/RPA.Studio/src/app/studio/designer/properties/text-offset-editor.component.spec.ts` (**Create**)

---

## Task 1: Domain kontratı — `ClickTextOffsetAsync`

**Files:**
- Modify: `src/RPA.Domain/Interfaces/IVisionAutomationChannel.cs`
- Modify: `src/RPA.Infrastructure/Activities/Vision/UnavailableVisionAutomationChannel.cs`
- Modify: `CLAUDE.md`

**Interfaces:**
- Produces: `Task IVisionAutomationChannel.ClickTextOffsetAsync(string anchorText, int dx, int dy, string language, string matchMode, string? clickType, int timeoutMs)`

- [ ] **Step 1: Arayüze metot ekle**

`IVisionAutomationChannel.cs` içinde `ClickTextAsync` bildiriminden sonra ekle:

```csharp
    /// <summary>
    /// OCR ile anchorText'i bulur, kelime kutusunun merkezinden (dx,dy) piksel ofsetle tıklar
    /// (etiketin yanındaki boş alan gibi kendi başına ayırt edilemeyen hedefler için).
    /// Çapa bulunamazsa/timeout → SystemException.
    /// </summary>
    Task ClickTextOffsetAsync(string anchorText, int dx, int dy,
        string language, string matchMode, string? clickType, int timeoutMs);
```

- [ ] **Step 2: `UnavailableVisionAutomationChannel`'a implementasyon ekle**

Mevcut metotların yanına (aynı "ajan yok" fırlatma desenini izleyerek — dosyadaki mevcut `Throw()`/mesaj desenini kullan):

```csharp
    public Task ClickTextOffsetAsync(string anchorText, int dx, int dy,
        string language, string matchMode, string? clickType, int timeoutMs)
        => throw Unavailable();
```

> Not: Dosyadaki mevcut helper adını (ör. `Unavailable()` veya inline `new SystemException(...)`) birebir kullan; diğer metotlar ne yapıyorsa aynısını yap.

- [ ] **Step 3: Derle**

Run: `dotnet build src/RPA.Infrastructure/RPA.Infrastructure.csproj`
Expected: BAŞARILI (TesseractOpenCvVisionChannel henüz implement etmediği için yalnız Infrastructure derlenir; Agent Task 3'te derlenecek — bu beklenen).

> Eğer solution genel derlemesi yapılırsa `TesseractOpenCvVisionChannel` "arayüz üyesi implement edilmemiş" hatası verir; bu Task 3'te giderilecektir. Bu task'ta yalnız Infrastructure projesini derle.

- [ ] **Step 4: CLAUDE.md kontrat notu ekle**

Dosyanın sonundaki son "Kontrat Değişikliği" bloğundan sonra ekle:

```markdown
---

## Kontrat Değişikliği — 2026-07-15 (Vision metin çapası ofset tıklama)

`IVisionAutomationChannel.ClickTextOffsetAsync(anchorText, dx, dy, language, matchMode, clickType, timeoutMs)`
eklendi — OCR metin çapasının kelime kutusu merkezinden piksel ofsetle tıklar (etiketin yanındaki
boş input gibi hedefler). Yeni aktivite `Vision.ClickTextOffset` (kategori "Görüntü", capability
`vision`). Yeni picker kind `text-offset` (iki aşamalı: çapa metni seç + hedef nokta tıkla → dx/dy
otomatik). `SpyElementMessage`'a `Kind="text-offset"`, `AnchorText`, `Dx`, `Dy` + `FromTextOffset`;
`StudioHub.SupportedKinds`'e `text-offset`. Referans: çapanın OCR tight kelime kutusu **merkezi**
(picker-zamanı ve runtime aynı) → çözünürlük farkında ofset kaymaz.

Etkilenen paketler: Paket F (Vision), Studio picker metadata tüketicileri, Agent UI Spy transport.
Gerekçe: erişilebilirlik ağacı olmayan ekranlarda etiket-yanı boş alanlara tıklama (UiPath CV
"anchor + relative offset" modeli).
```

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Domain/Interfaces/IVisionAutomationChannel.cs src/RPA.Infrastructure/Activities/Vision/UnavailableVisionAutomationChannel.cs CLAUDE.md
git commit -m "refactor(contract): IVisionAutomationChannel.ClickTextOffsetAsync

Metin capasi + piksel ofset tiklama sozlesmesi. Unavailable impl firlatir.
Kontrat Degisikligi (CLAUDE.md).

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

## Task 2: Aktivite + parse + kayıt (`Vision.ClickTextOffset`)

**Files:**
- Modify: `src/RPA.Infrastructure/Activities/Vision/VisionActivities.cs`
- Modify: `src/RPA.Infrastructure/Workflow/WorkflowServiceCollectionExtensions.cs`
- Modify: `src/RPA.Infrastructure/Workflow/ActivityRegistry.cs`
- Test: `tests/RPA.Infrastructure.Tests/Activities/VisionActivitiesTests.cs`
- Test: `tests/RPA.Infrastructure.Tests/Workflow/VisionCatalogTests.cs`

**Interfaces:**
- Consumes: `IVisionAutomationChannel.ClickTextOffsetAsync` (Task 1).
- Produces: `VisionClickTextOffsetActivity : IActivity` (ActivityId `Vision.ClickTextOffset`); tek birleşik input `anchor` (JSON `{anchorText,dx,dy}`, `PickerKind="text-offset"`) + `language`,`matchMode`,`clickType`,`timeoutMs`.

- [ ] **Step 1: Başarısız testleri yaz**

`VisionActivitiesTests.cs`'e ekle:

```csharp
    [Fact]
    public async Task ClickTextOffset_EmptyAnchorText_ThrowsBusiness()
    {
        var channel = new Mock<IVisionAutomationChannel>();
        var activity = new VisionClickTextOffsetActivity(channel.Object);
        var ctx = Ctx(new() { ["anchor"] = "{\"anchorText\":\"\",\"dx\":10,\"dy\":0}" });

        await Assert.ThrowsAsync<BusinessException>(() => activity.ExecuteAsync(ctx.Object));
    }

    [Fact]
    public async Task ClickTextOffset_InvalidJson_ThrowsBusiness()
    {
        var channel = new Mock<IVisionAutomationChannel>();
        var activity = new VisionClickTextOffsetActivity(channel.Object);
        var ctx = Ctx(new() { ["anchor"] = "not-json" });

        await Assert.ThrowsAsync<BusinessException>(() => activity.ExecuteAsync(ctx.Object));
    }

    [Fact]
    public async Task ClickTextOffset_Valid_CallsChannelWithParsedValuesAndDefaults()
    {
        var channel = new Mock<IVisionAutomationChannel>();
        var activity = new VisionClickTextOffsetActivity(channel.Object);
        var ctx = Ctx(new()
        {
            ["anchor"] = "{\"anchorText\":\"Malzeme No\",\"dx\":120,\"dy\":-4}",
            ["timeoutMs"] = 0,
        });

        await activity.ExecuteAsync(ctx.Object);

        // language boş → tur+eng; matchMode boş → contains; clickType null → left kanala bırakılır; timeoutMs 0 → 5000
        channel.Verify(c => c.ClickTextOffsetAsync(
            "Malzeme No", 120, -4, "tur+eng", "contains", null, 5000), Times.Once);
    }
```

- [ ] **Step 2: Testleri çalıştır (FAIL)**

Run: `dotnet test tests/RPA.Infrastructure.Tests --filter FullyQualifiedName~ClickTextOffset`
Expected: FAIL — `VisionClickTextOffsetActivity` tanımlı değil.

- [ ] **Step 3: Aktiviteyi ve parse'ı yaz**

`VisionActivities.cs` sonuna (VisionRegion record'undan önce) ekle:

```csharp
/// <summary>Ekranda OCR metin çapasını bulur, kelime kutusunun merkezinden (dx,dy) ofsetle tıklar.</summary>
public sealed class VisionClickTextOffsetActivity : IActivity
{
    private readonly IVisionAutomationChannel _channel;
    public VisionClickTextOffsetActivity(IVisionAutomationChannel channel)
        => _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public ActivityMetadata GetMetadata() => new()
    {
        ActivityId = "Vision.ClickTextOffset",
        DisplayName = "Çapaya Göre Tıkla (OCR)",
        Category = VisionMeta.Category,
        Description = "OCR ile bir metin çapasını bulur ve ondan piksel ofsetle tıklar " +
                      "(etiketin yanındaki boş alan gibi hedefler). 🎯 ile çapa + hedef nokta seçin.",
        Inputs = new()
        {
            new ActivityParameter
            {
                Name = "anchor", Type = "string", Required = true,
                Description = "Çapa + ofset (JSON): {anchorText, dx, dy}. 🎯 ile oluşturun.",
                PickerKind = "text-offset",
            },
            VisionMeta.Language(),
            VisionMeta.MatchMode(),
            VisionMeta.ClickType(),
            new ActivityParameter { Name = "timeoutMs", Type = "int", Required = false, Description = "Zaman aşımı (ms).", DefaultValue = 5000 },
        },
        Outputs = new(),
        RequiredCapabilities = new() { VisionMeta.Capability },
        ExceptionClassification = new ExceptionClassificationRule { Condition = "Timeout", Classification = RPA.Domain.Enums.ExceptionType.System },
    };

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var spec = TextOffsetSpec.Parse(context.GetVariable<string?>("anchor"));
        var language = VisionMeta.LanguageOrDefault(context);
        var matchMode = VisionMeta.MatchModeOrDefault(context);
        var clickType = VisionMeta.ClickTypeOrNull(context);
        var timeoutMs = context.GetVariable<int>("timeoutMs");
        if (timeoutMs <= 0)
        {
            timeoutMs = 5000;
        }
        context.Log($"Çapaya göre tıklanıyor: '{spec.AnchorText}' ofset ({spec.Dx},{spec.Dy}).");
        await _channel.ClickTextOffsetAsync(spec.AnchorText, spec.Dx, spec.Dy, language, matchMode, clickType, timeoutMs);
        return new();
    }
}

/// <summary>Çapa+ofset spesifikasyonu: OCR ile bulunacak metin ve kelime kutusu merkezinden ofset.</summary>
internal readonly record struct TextOffsetSpec(string AnchorText, int Dx, int Dy)
{
    public static TextOffsetSpec Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new BusinessException("'anchor' parametresi boş olamaz.");
        }
        System.Text.Json.JsonDocument doc;
        try
        {
            doc = System.Text.Json.JsonDocument.Parse(json);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new BusinessException($"'anchor' geçerli bir JSON değil: {ex.Message}");
        }
        using (doc)
        {
            var root = doc.RootElement;
            var text = root.TryGetProperty("anchorText", out var t) ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new BusinessException("'anchorText' boş olamaz.");
            }
            var dx = root.TryGetProperty("dx", out var x) && x.TryGetInt32(out var xi) ? xi : 0;
            var dy = root.TryGetProperty("dy", out var y) && y.TryGetInt32(out var yi) ? yi : 0;
            return new TextOffsetSpec(text, dx, dy);
        }
    }
}
```

- [ ] **Step 4: Testleri çalıştır (PASS)**

Run: `dotnet test tests/RPA.Infrastructure.Tests --filter FullyQualifiedName~ClickTextOffset`
Expected: PASS (3 test).

- [ ] **Step 5: Keyed DI kaydı ekle**

`WorkflowServiceCollectionExtensions.cs`'te `Vision.ClickText` kaydının yanına ekle:

```csharp
        services.AddKeyedTransient<IActivity, RPA.Infrastructure.Activities.Vision.VisionClickTextOffsetActivity>("Vision.ClickTextOffset");
```

- [ ] **Step 6: Katalog girişi ekle**

`ActivityRegistry.cs` `RegisterVision` içinde `Vision.ClickText` girişinden sonra ekle:

```csharp
        b.Activity("Vision.ClickTextOffset").DisplayName("Çapaya Göre Tıkla (OCR)").Category(CatVision).Capability(cap)
            .Description("OCR metin çapasından piksel ofsetle tıklar (etiket-yanı boş alanlar).")
            .Input("anchor", "string", pickerKind: "text-offset", description: "Çapa + ofset (JSON): {anchorText, dx, dy}.")
            .Input("language", "string", required: false, defaultValue: "tur+eng")
            .Input("matchMode", "string", required: false, defaultValue: "contains", options: new[] { "contains", "exact" })
            .Input("clickType", "string", required: false, defaultValue: "left", options: new[] { "left", "right", "double" })
            .Input("timeoutMs", "int", required: false, defaultValue: 5000)
            .ExceptionClassification("Timeout", ExceptionType.System);
```

- [ ] **Step 7: Katalog testini güncelle**

`VisionCatalogTests.cs`'te `[InlineData]` listesine ekle ve sayaç 7→8 yap:

```csharp
    [InlineData("Vision.ClickTextOffset")]
```
ve `VisionActivities_HaveVisionCapability` içinde:
```csharp
        Assert.Equal(8, vision.Count);
```

- [ ] **Step 8: Testleri çalıştır (PASS)**

Run: `dotnet test tests/RPA.Infrastructure.Tests --filter FullyQualifiedName~Vision`
Expected: PASS (tüm Vision testleri).

- [ ] **Step 9: Commit**

```bash
git add src/RPA.Infrastructure tests/RPA.Infrastructure.Tests
git commit -m "feat(vision): Vision.ClickTextOffset aktivitesi + katalog/DI

Metin capasi + piksel ofset tiklama; anchor JSON parse; keyed DI + katalog.

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

## Task 3: Kanal implementasyonu + OCR/ofset yardımcıları (Agent)

**Files:**
- Create: `src/RPA.Agent/Vision/OcrEngine.cs`
- Create: `src/RPA.Agent/Vision/VisionOffset.cs`
- Modify: `src/RPA.Agent/Vision/TesseractOpenCvVisionChannel.cs`
- Test: `tests/RPA.Agent.Tests/Vision/VisionOffsetTests.cs`

**Interfaces:**
- Consumes: `IVisionAutomationChannel.ClickTextOffsetAsync` (implement); `VisionMatch.CenterX/CenterY`; `OcrTextMatch.Matches`.
- Produces: `VisionOffset.ClickPoint(VisionMatch anchorBox, int dx, int dy) → (int X, int Y)`; `OcrEngine.ReadWords(Mat image, string tessdataPath, string language) → IReadOnlyList<(string Text, VisionMatch Box)>`.

- [ ] **Step 1: Ofset yardımcı testini yaz**

`tests/RPA.Agent.Tests/Vision/VisionOffsetTests.cs` oluştur:

```csharp
namespace RPA.Agent.Tests.Vision;

using RPA.Agent.Vision;
using RPA.Domain.ValueObjects;
using Xunit;

public class VisionOffsetTests
{
    [Fact]
    public void ClickPoint_UsesAnchorBoxCenterPlusOffset()
    {
        // Kutu (x=100,y=200,w=40,h=20) → merkez (120,210); ofset (+50,-10) → (170,200)
        var box = new VisionMatch(100, 200, 40, 20, 1.0);
        var (x, y) = VisionOffset.ClickPoint(box, 50, -10);
        Assert.Equal(170, x);
        Assert.Equal(200, y);
    }

    [Fact]
    public void ClickPoint_ZeroOffset_IsCenter()
    {
        var box = new VisionMatch(0, 0, 10, 10, 1.0);
        var (x, y) = VisionOffset.ClickPoint(box, 0, 0);
        Assert.Equal(5, x);
        Assert.Equal(5, y);
    }
}
```

- [ ] **Step 2: Testi çalıştır (FAIL)**

Run: `dotnet test tests/RPA.Agent.Tests --filter FullyQualifiedName~VisionOffset`
Expected: FAIL — `VisionOffset` tanımlı değil.

- [ ] **Step 3: `VisionOffset` yardımcısını yaz**

`src/RPA.Agent/Vision/VisionOffset.cs` oluştur:

```csharp
namespace RPA.Agent.Vision;

using RPA.Domain.ValueObjects;

/// <summary>Çapa kelime kutusunun merkezinden piksel ofsetle tıklama noktasını hesaplar.
/// Picker-zamanı ve runtime aynı referansı (tight OCR kutusu merkezi) kullanır.</summary>
public static class VisionOffset
{
    public static (int X, int Y) ClickPoint(VisionMatch anchorBox, int dx, int dy)
        => (anchorBox.CenterX + dx, anchorBox.CenterY + dy);
}
```

- [ ] **Step 4: Testi çalıştır (PASS)**

Run: `dotnet test tests/RPA.Agent.Tests --filter FullyQualifiedName~VisionOffset`
Expected: PASS (2 test).

- [ ] **Step 5: `OcrEngine` yardımcısını çıkar**

`src/RPA.Agent/Vision/OcrEngine.cs` oluştur (mevcut `TesseractOpenCvVisionChannel.RunOcr` mantığını paylaşılabilir kılar):

```csharp
namespace RPA.Agent.Vision;

using System.Runtime.Versioning;
using OpenCvSharp;
using RPA.Domain.ValueObjects;
using Tesseract;

/// <summary>Tesseract OCR — bir görüntüden tam metni ve kelime kutularını çıkarır.
/// Kanal ve text-offset picker tarafından paylaşılır.</summary>
[SupportedOSPlatform("windows")]
public static class OcrEngine
{
    public sealed record OcrWord(string Text, VisionMatch Box);

    public static (string Text, List<OcrWord> Words) Read(Mat image, string tessdataPath, string language)
    {
        using var engine = new TesseractEngine(tessdataPath, language, EngineMode.Default);
        var bytes = image.ImEncode(".png");
        using var pix = Pix.LoadFromMemory(bytes);
        using var page = engine.Process(pix);
        var full = page.GetText() ?? string.Empty;

        var words = new List<OcrWord>();
        using var iter = page.GetIterator();
        iter.Begin();
        do
        {
            if (iter.TryGetBoundingBox(PageIteratorLevel.Word, out var r))
            {
                var w = iter.GetText(PageIteratorLevel.Word);
                words.Add(new OcrWord(w ?? string.Empty, new VisionMatch(r.X1, r.Y1, r.Width, r.Height, 1.0)));
            }
        }
        while (iter.Next(PageIteratorLevel.Word));
        return (full, words);
    }
}
```

- [ ] **Step 6: `TesseractOpenCvVisionChannel`'ı OcrEngine kullanacak şekilde güncelle ve yeni metodu ekle**

`RunOcr` metodunu OcrEngine'e delege et (imza korunur, iç gövde değişir):

```csharp
    private (string Text, List<OcrWord> Words) RunOcr(Mat image, string language)
    {
        try
        {
            var (full, words) = OcrEngine.Read(image, _tessdataPath, language);
            return (full, words.Select(w => new OcrWord(w.Text, w.Box)).ToList());
        }
        catch (Exception ex) when (ex is not RPA.Domain.Exceptions.SystemException)
        {
            throw new SystemException($"OCR başarısız: {ex.Message}", ex);
        }
    }
```

`ClickTextAsync`'in yanına yeni metodu ekle:

```csharp
    public async Task ClickTextOffsetAsync(string anchorText, int dx, int dy,
        string language, string matchMode, string? clickType, int timeoutMs)
    {
        var box = await PollForTextAsync(anchorText, language, matchMode, timeoutMs);
        if (box is null)
        {
            throw new SystemException($"Çapa metni ekranda bulunamadı: '{anchorText}' (timeout).");
        }
        var (x, y) = VisionOffset.ClickPoint(box, dx, dy);
        DoClick(x, y, clickType);
    }
```

- [ ] **Step 7: Agent'ı derle + testleri çalıştır**

Run: `dotnet build src/RPA.Agent/RPA.Agent.csproj` ardından `dotnet test tests/RPA.Agent.Tests --filter FullyQualifiedName~Vision`
Expected: BAŞARILI derleme (artık `IVisionAutomationChannel` tümüyle implement edildi) + PASS.

- [ ] **Step 8: Commit**

```bash
git add src/RPA.Agent/Vision tests/RPA.Agent.Tests/Vision
git commit -m "feat(vision): ClickTextOffset kanal impl + OcrEngine/VisionOffset

Capayi OCR ile bul, kelime kutusu merkezinden ofsetle tikla. OCR mantigi
OcrEngine'e cikarildi (picker ile paylasilir).

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

## Task 4: Mesaj sözleşmesi + koordinatör dalı + picker arayüzü

**Files:**
- Modify: `src/RPA.Infrastructure/UISpy/SapGuiElementSender.cs`
- Modify: `src/RPA.Agent/UISpy/SpySessionCoordinator.cs`
- Test: `tests/RPA.Infrastructure.Tests/UISpy/SpyElementMessageTests.cs`
- Test: `tests/RPA.Agent.Tests/UISpy/SpySessionCoordinatorTextOffsetTests.cs`

**Interfaces:**
- Consumes: `SpySessionCoordinator` yapıcısı (mevcut opsiyonel picker deseni).
- Produces:
  - `SpyElementMessage.FromTextOffset(string anchorText, int dx, int dy, string? previewBase64, Guid sessionId)`; yeni alanlar `AnchorText` (string?), `Dx` (int?), `Dy` (int?).
  - `interface ITextOffsetPicker { Task<TextOffsetPick?> DetectOnceAsync(ImagePickerOptions options, CancellationToken ct); }`
  - `sealed record TextOffsetPick(string AnchorText, int Dx, int Dy, string? PreviewBase64);`
  - `SpySessionCoordinator` yeni opsiyonel `ITextOffsetPicker? textOffsetPicker` parametresi + `text-offset` dalı.

- [ ] **Step 1: Mesaj testini yaz**

`SpyElementMessageTests.cs`'e ekle:

```csharp
    [Fact]
    public void FromTextOffset_SetsKindAndFields()
    {
        var sid = Guid.NewGuid();
        var msg = SpyElementMessage.FromTextOffset("Malzeme No", 120, -4, "PNG64", sid);

        Assert.Equal("text-offset", msg.Kind);
        Assert.Equal(sid, msg.SessionId);
        Assert.Equal("Malzeme No", msg.AnchorText);
        Assert.Equal(120, msg.Dx);
        Assert.Equal(-4, msg.Dy);
        Assert.Equal("PNG64", msg.ImageBase64);
    }
```

- [ ] **Step 2: Testi çalıştır (FAIL)**

Run: `dotnet test tests/RPA.Infrastructure.Tests --filter FullyQualifiedName~FromTextOffset`
Expected: FAIL — `FromTextOffset` yok.

- [ ] **Step 3: Mesaja alanları ve fabrikayı ekle**

`SpyElementMessage` record'una (mevcut `Region` init'inden sonra) ekle:

```csharp
    public string? AnchorText { get; init; }
    public int? Dx { get; init; }
    public int? Dy { get; init; }
```

`FromImage` metodunun altına ekle:

```csharp
    /// <summary>🎯 text-offset picker'dan (çapa metni + hedef nokta) mesaj oluşturur.</summary>
    public static SpyElementMessage FromTextOffset(string anchorText, int dx, int dy, string? previewBase64, Guid sessionId)
    {
        return new SpyElementMessage
        {
            SessionId = sessionId,
            Kind = "text-offset",
            ElementId = "text-offset",
            AnchorText = anchorText,
            Dx = dx,
            Dy = dy,
            ImageBase64 = previewBase64,
            Text = anchorText,
            Selector = anchorText,
            Enabled = true,
            Changeable = true,
        };
    }
```

- [ ] **Step 4: Mesaj testini çalıştır (PASS)**

Run: `dotnet test tests/RPA.Infrastructure.Tests --filter FullyQualifiedName~FromTextOffset`
Expected: PASS.

- [ ] **Step 5: Koordinatör testini yaz**

`tests/RPA.Agent.Tests/UISpy/SpySessionCoordinatorTextOffsetTests.cs` oluştur:

```csharp
namespace RPA.Agent.Tests.UISpy;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RPA.Agent.UISpy;
using RPA.Infrastructure.UISpy;
using Xunit;

public class SpySessionCoordinatorTextOffsetTests
{
    [Fact]
    public async Task Start_TextOffsetKind_SendsTextOffsetMessage()
    {
        var sessionId = Guid.NewGuid();
        var transport = new Mock<ISpyElementTransport>();
        var picker = new Mock<ITextOffsetPicker>();
        picker.Setup(p => p.DetectOnceAsync(It.IsAny<ImagePickerOptions>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new TextOffsetPick("Malzeme No", 120, -4, "PNG64"));
        var options = Options.Create(new SpySessionOptions { TimeoutSeconds = 5 });

        var coordinator = new SpySessionCoordinator(
            Mock.Of<ISapGuiSinglePicker>(), transport.Object, options,
            NullLogger<SpySessionCoordinator>.Instance,
            textOffsetPicker: picker.Object);

        await coordinator.StartAsync(sessionId, "text-offset", "{\"captureMode\":\"timer\",\"delaySeconds\":8}");

        transport.Verify(t => t.SendAsync(
            It.Is<SpyElementMessage>(m => m.Kind == "text-offset" && m.AnchorText == "Malzeme No" && m.Dx == 120 && m.Dy == -4),
            It.IsAny<CancellationToken>()), Times.Once);
        picker.Verify(p => p.DetectOnceAsync(
            It.Is<ImagePickerOptions>(o => o.CaptureMode == "timer" && o.DelaySeconds == 8),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Start_TextOffsetKind_NoPicker_Throws()
    {
        var coordinator = new SpySessionCoordinator(
            Mock.Of<ISapGuiSinglePicker>(), Mock.Of<ISpyElementTransport>(),
            Options.Create(new SpySessionOptions()), NullLogger<SpySessionCoordinator>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.StartAsync(Guid.NewGuid(), "text-offset"));
    }
}
```

- [ ] **Step 6: Testi çalıştır (FAIL)**

Run: `dotnet test tests/RPA.Agent.Tests --filter FullyQualifiedName~TextOffset`
Expected: FAIL — `ITextOffsetPicker`/`TextOffsetPick` yok.

- [ ] **Step 7: Arayüz + record + koordinatör dalını ekle**

`SpySessionCoordinator.cs`'te `IImageRegionPicker` arayüzünün yanına ekle:

```csharp
/// <summary>🎯 text-offset picker'ı — çapa metnini seç + hedef noktaya tıkla, dx/dy hesapla.</summary>
public interface ITextOffsetPicker
{
    Task<TextOffsetPick?> DetectOnceAsync(ImagePickerOptions options, CancellationToken cancellationToken = default);
}

/// <summary>text-offset picker sonucu: çapa metni, dx/dy ofset ve çapa önizleme (base64 PNG).</summary>
public sealed record TextOffsetPick(string AnchorText, int Dx, int Dy, string? PreviewBase64);
```

Yapıcıya alan + parametre ekle (mevcut `_imagePicker` desenini izle):

```csharp
    private readonly ITextOffsetPicker? _textOffsetPicker;
```
Yapıcı imzasına son parametre olarak:
```csharp
        IImageRegionPicker? imagePicker = null,
        ITextOffsetPicker? textOffsetPicker = null)
```
ve gövdeye:
```csharp
        _textOffsetPicker = textOffsetPicker;
```

`StartAsync` içindeki kind çözümlemesine ekle:

```csharp
        var isTextOffset = string.Equals(kind, "text-offset", StringComparison.OrdinalIgnoreCase);
```
`if (!isSap && !isDesktop && !isWeb && !isImage)` koşulunu güncelle:
```csharp
        if (!isSap && !isDesktop && !isWeb && !isImage && !isTextOffset)
```
`isImage` picker null kontrolünün yanına:
```csharp
        if (isTextOffset && _textOffsetPicker is null)
        {
            throw new InvalidOperationException("Metin-ofset picker bu ortamda kayıtlı değil (yalnız Windows).");
        }
```
Uzun timeout bloğunu güncelle (image gibi manuel dondurma gerekir):
```csharp
            if (isImage || isTextOffset)
            {
                timeoutSeconds = Math.Max(timeoutSeconds, 300);
            }
```
Mesaj üretim `else if (isImage)` dalından sonra ekle:
```csharp
            else if (isTextOffset)
            {
                var pickerOptions = ImagePickerOptions.Parse(optionsJson);
                var pick = await _textOffsetPicker!.DetectOnceAsync(pickerOptions, linkedCts.Token);
                message = pick is null ? null : SpyElementMessage.FromTextOffset(pick.AnchorText, pick.Dx, pick.Dy, pick.PreviewBase64, sessionId);
            }
```

- [ ] **Step 8: Testleri çalıştır (PASS)**

Run: `dotnet test tests/RPA.Agent.Tests --filter FullyQualifiedName~TextOffset`
Expected: PASS (2 test).

- [ ] **Step 9: Commit**

```bash
git add src/RPA.Infrastructure/UISpy/SapGuiElementSender.cs src/RPA.Agent/UISpy/SpySessionCoordinator.cs tests/RPA.Infrastructure.Tests/UISpy/SpyElementMessageTests.cs tests/RPA.Agent.Tests/UISpy/SpySessionCoordinatorTextOffsetTests.cs
git commit -m "feat(spy): text-offset mesaj + koordinator dali + ITextOffsetPicker

SpyElementMessage.FromTextOffset, koordinatore text-offset kind dali.

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

## Task 5: GDI text-offset picker (Agent, iki aşamalı)

**Files:**
- Create: `src/RPA.Agent/UISpy/GdiTextOffsetPicker.cs`
- Modify: `src/RPA.Agent/AgentServiceCollectionExtensions.cs`

**Interfaces:**
- Consumes: `ITextOffsetPicker`, `TextOffsetPick` (Task 4); `OcrEngine.Read` (Task 3); `ImagePickerOptions`; `ScreenCapture.CaptureVirtualScreenBitmap`, `ScreenCapture.VirtualScreenOrigin`; `ArmForm.WaitAndCapture`, `SelectionForm.SelectOnSnapshot` (mevcut `GdiImageRegionPicker.cs` içindeki internal tipler — aynı assembly).
- Produces: `GdiTextOffsetPicker : ITextOffsetPicker`.

> Bu task birim-test edilmez (GDI/STA/OCR yan etkileri; mevcut `GdiImageRegionPicker` deseniyle aynı). Deliverable: derlenir + DI'a kayıtlı.

- [ ] **Step 1: Picker'ı yaz**

`src/RPA.Agent/UISpy/GdiTextOffsetPicker.cs` oluştur. Akış: (1) ArmForm ile ekranı dondur; (2) `SelectionForm.SelectOnSnapshot` ile çapa dikdörtgenini seç; (3) çapa kırpıntısını OCR et → en iyi kelime + tight kutu (snapshot koordinatına ötele); (4) ikinci aşama: hedef noktaya tek tık (`ClickPointForm`); (5) dx/dy = tık − çapaKutuMerkezi.

```csharp
namespace RPA.Agent.UISpy;

using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using System.Windows.Forms;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using RPA.Agent.Vision;
using RPA.Domain.ValueObjects;

/// <summary>
/// İki aşamalı görsel text-offset picker. GdiImageRegionPicker'ın freeze (ArmForm) + seçim
/// (SelectionForm) altyapısını yeniden kullanır: (1) ekranı dondur; (2) çapa etiketinin çevresine
/// dikdörtgen çiz → kırpıntı OCR edilir (çapa metni + tight kutu); (3) hedef noktaya tek tık →
/// dx/dy = tık − çapa kutusu merkezi. İptal (Esc / seçim yok / OCR boş) → null.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GdiTextOffsetPicker : ITextOffsetPicker
{
    private readonly string _tessdataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");

    public Task<TextOffsetPick?> DetectOnceAsync(ImagePickerOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TextOffsetPick? result = null;

        var thread = new Thread(() =>
        {
            using var snapshot = ArmForm.WaitAndCapture(options, cancellationToken);
            if (snapshot is null)
            {
                return; // iptal / timeout
            }

            // 1) Çapa dikdörtgeni (donmuş görüntü client koordinatı = snapshot pikseli).
            var anchorRect = SelectionForm.SelectOnSnapshot(snapshot, cancellationToken);
            if (anchorRect is null || anchorRect.Value.Width < 2 || anchorRect.Value.Height < 2)
            {
                return;
            }
            var ar = anchorRect.Value;

            // 2) Çapa kırpıntısını OCR et → en iyi (en geniş) kelime kutusu.
            string anchorText;
            VisionMatch anchorBoxOnSnapshot;
            using (var crop = snapshot.Clone(ar, snapshot.PixelFormat))
            using (var mat = BitmapConverter.ToMat(crop))
            {
                var (_, words) = OcrEngine.Read(mat, _tessdataPath, "tur+eng");
                var best = words
                    .Where(w => !string.IsNullOrWhiteSpace(w.Text))
                    .OrderByDescending(w => w.Box.Width * w.Box.Height)
                    .FirstOrDefault();
                if (best is null)
                {
                    return; // OCR metin bulamadı → iptal
                }
                anchorText = best.Text.Trim();
                // Kırpıntı-yerel kutuyu snapshot koordinatına ötele.
                anchorBoxOnSnapshot = new VisionMatch(
                    ar.X + best.Box.X, ar.Y + best.Box.Y, best.Box.Width, best.Box.Height, 1.0);
            }

            // 3) Hedef noktaya tek tık (aynı donmuş görüntü üzerinde).
            var target = ClickPointForm.PickPoint(snapshot, cancellationToken);
            if (target is null)
            {
                return;
            }

            var dx = target.Value.X - anchorBoxOnSnapshot.CenterX;
            var dy = target.Value.Y - anchorBoxOnSnapshot.CenterY;

            // Önizleme: çapa kırpıntısının PNG base64'ü.
            string preview;
            using (var crop = snapshot.Clone(ar, snapshot.PixelFormat))
            using (var ms = new MemoryStream())
            {
                crop.Save(ms, ImageFormat.Png);
                preview = Convert.ToBase64String(ms.ToArray());
            }

            result = new TextOffsetPick(anchorText, dx, dy, preview);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        return Task.FromResult(result);
    }
}

/// <summary>
/// Donmuş görüntü üzerinde tek nokta seçtiren form. Tıklanan client noktasını (snapshot pikseli)
/// döndürür, Esc iptal eder. SelectionForm ile aynı foreground/topmost hilelerini kullanır.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ClickPointForm : Form
{
    private Point? _picked;

    private ClickPointForm(Bitmap snapshot)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = SystemInformation.VirtualScreen;
        TopMost = true;
        DoubleBuffered = true;
        Cursor = Cursors.Cross;
        ShowInTaskbar = false;
        KeyPreview = true;
        BackgroundImage = snapshot;
        BackgroundImageLayout = ImageLayout.None;

        MouseDown += (_, e) => { _picked = e.Location; Close(); };
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { _picked = null; Close(); } };
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        NativeForeground.ForceForeground(Handle);
        Activate();
    }

    public static Point? PickPoint(Bitmap snapshot, CancellationToken cancellationToken)
    {
        using var form = new ClickPointForm(snapshot);
        using var reg = cancellationToken.Register(() =>
        {
            try
            {
                if (form.IsHandleCreated)
                {
                    form.BeginInvoke(new Action(form.Close));
                }
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        });

        Application.Run(form);
        return form._picked;
    }
}
```

> `BitmapConverter.ToMat` `OpenCvSharp.Extensions` içindedir (Agent zaten OpenCvSharp kullanıyor). `ArmForm`, `SelectionForm`, `NativeForeground` `GdiImageRegionPicker.cs`'te `internal`; aynı assembly (`RPA.Agent`) olduğundan erişilebilir.

- [ ] **Step 2: DI kaydı ekle**

`AgentServiceCollectionExtensions.cs`'te `IImageRegionPicker` kaydının yanına (Windows koşulu bloğu içinde) ekle:

```csharp
            services.AddSingleton<ITextOffsetPicker, GdiTextOffsetPicker>();
```

- [ ] **Step 3: Agent'ı derle**

Run: `dotnet build src/RPA.Agent/RPA.Agent.csproj`
Expected: BAŞARILI.

- [ ] **Step 4: Agent testlerini çalıştır (regresyon)**

Run: `dotnet test tests/RPA.Agent.Tests`
Expected: PASS (mevcut + yeni testler; picker'ın kendisi test edilmez).

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Agent/UISpy/GdiTextOffsetPicker.cs src/RPA.Agent/AgentServiceCollectionExtensions.cs
git commit -m "feat(spy): GdiTextOffsetPicker — iki asamali gorsel capa+ofset secimi

Freeze → capa dikdortgeni (OCR) → hedef nokta → dx/dy. AddAgentCore kaydi.

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

## Task 6: WebAPI StudioHub — `text-offset` whitelist

**Files:**
- Modify: `src/RPA.WebAPI/Hubs/StudioHub.cs`
- Test: `src/RPA.WebAPI` mevcut hub testi varsa güncelle (yoksa atlanır).

**Interfaces:**
- Consumes: `StudioHub.SupportedKinds`.

- [ ] **Step 1: Whitelist'e ekle**

`StudioHub.cs`'te `SupportedKinds` set'ine `"image"`'in yanına ekle:

```csharp
        "text-offset",
```

- [ ] **Step 2: Derle + WebAPI testlerini çalıştır**

Run: `dotnet test tests/RPA.WebAPI.Tests --filter FullyQualifiedName~Spy`
Expected: PASS (mevcut StartSpy testleri; yeni kind red edilmiyor).

> Eğer `StudioHub` "desteklenen kind" için parametreli bir test içeriyorsa (`Theory` InlineData), `"text-offset"` satırı ekle.

- [ ] **Step 3: Commit**

```bash
git add src/RPA.WebAPI/Hubs/StudioHub.cs
git commit -m "feat(hub): StudioHub text-offset spy kind whitelist

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

## Task 7: Studio — spy servisi + text-offset editörü + dispatch + i18n

**Files:**
- Modify: `src/RPA.Studio/src/app/shared/services/spy.service.ts`
- Create: `src/RPA.Studio/src/app/studio/designer/properties/text-offset-editor.component.ts` (+`.html`,`.scss`)
- Modify: `src/RPA.Studio/src/app/studio/designer/properties/generic-property.component.ts` (+ `.html`)
- Modify: `src/RPA.Studio/src/assets/i18n/tr.json` (+ varsa `en.json`)
- Test: `src/RPA.Studio/src/app/studio/designer/properties/text-offset-editor.component.spec.ts`

**Interfaces:**
- Consumes: `SpyService.pick(kind, options?)`, `SpyElement` (`anchorText?`, `dx?`, `dy?`).
- Produces: `TextOffsetEditorComponent` (`@Input() value`, `@Output() valueChange` — JSON `{anchorText,dx,dy}`).

- [ ] **Step 1: SpyKind + SpyElement alanlarını genişlet**

`spy.service.ts`:
```typescript
export type SpyKind = 'sap' | 'web' | 'desktop' | 'image' | 'text-offset';
```
`SpyElement` arayüzüne ekle:
```typescript
  // text-offset picker sonucu (kind === 'text-offset').
  anchorText?: string;
  dx?: number;
  dy?: number;
```
`pick(...)` içindeki uzun-timeout ve optionsJson koşullarını `text-offset`'i de kapsayacak şekilde güncelle:
```typescript
    const needsFreeze = kind === 'image' || kind === 'text-offset';
    const timeoutMs = needsFreeze ? Math.max(this.timeoutMs, 360000) : this.timeoutMs;
```
```typescript
    const optionsJson = needsFreeze && options ? JSON.stringify(options) : null;
```

- [ ] **Step 2: Editör bileşeni testini yaz**

`text-offset-editor.component.spec.ts` oluştur:

```typescript
import { TextOffsetEditorComponent } from './text-offset-editor.component';

describe('TextOffsetEditorComponent', () => {
  let component: TextOffsetEditorComponent;

  beforeEach(() => {
    component = new TextOffsetEditorComponent();
  });

  it('parses incoming JSON value', () => {
    component.value = '{"anchorText":"Malzeme No","dx":120,"dy":-4}';
    expect(component.anchorText).toBe('Malzeme No');
    expect(component.dx).toBe(120);
    expect(component.dy).toBe(-4);
  });

  it('emits JSON on field change', () => {
    const emitted: string[] = [];
    component.valueChange.subscribe((v) => emitted.push(v));
    component.anchorText = 'Miktar';
    component.dx = 50;
    component.dy = 0;
    component.emit();
    expect(JSON.parse(emitted[emitted.length - 1])).toEqual({ anchorText: 'Miktar', dx: 50, dy: 0 });
  });

  it('applies picker result (anchorText + dx/dy)', () => {
    const emitted: string[] = [];
    component.valueChange.subscribe((v) => emitted.push(v));
    component.onPicked({ sessionId: 's', kind: 'text-offset', elementId: 'text-offset', anchorText: 'Tutar', dx: 200, dy: 10 });
    expect(JSON.parse(emitted[emitted.length - 1])).toEqual({ anchorText: 'Tutar', dx: 200, dy: 10 });
  });
});
```

- [ ] **Step 3: Testi çalıştır (FAIL)**

Run: `cd src/RPA.Studio && npx jest text-offset-editor`
Expected: FAIL — bileşen yok.

- [ ] **Step 4: Editör bileşenini yaz**

`text-offset-editor.component.ts`:

```typescript
import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '../../../core/translate.pipe';
import { SpyElement, SpyService, ImagePickerOptions } from '../../../shared/services/spy.service';

/**
 * Vision.ClickTextOffset için çapa+ofset editörü. 🎯 ile iki aşamalı picker'ı (text-offset) çağırır:
 * çapa metni + hedef nokta seçilir, dx/dy otomatik hesaplanır. Alanlar elle de düzeltilebilir.
 * Değer backend'e JSON {anchorText,dx,dy} olarak verilir.
 */
@Component({
  selector: 'app-text-offset-editor',
  standalone: true,
  imports: [CommonModule, FormsModule, TranslatePipe],
  templateUrl: './text-offset-editor.component.html',
  styleUrls: ['./text-offset-editor.component.scss'],
})
export class TextOffsetEditorComponent {
  private readonly spy = inject(SpyService, { optional: true });

  anchorText = '';
  dx = 0;
  dy = 0;
  preview: string | null = null;
  picking = false;
  error: string | null = null;

  @Input()
  set value(v: unknown) {
    const parsed = this.parse(typeof v === 'string' ? v : '');
    this.anchorText = parsed.anchorText;
    this.dx = parsed.dx;
    this.dy = parsed.dy;
  }

  @Output() readonly valueChange = new EventEmitter<string>();

  async pick(): Promise<void> {
    if (!this.spy || this.picking) {
      return;
    }
    this.picking = true;
    this.error = null;
    const options: ImagePickerOptions = { captureMode: 'f2', delaySeconds: 5 };
    try {
      const element = await this.spy.pick('text-offset', options);
      this.onPicked(element);
    } catch (e) {
      this.error = e instanceof Error ? e.message : String(e);
    } finally {
      this.picking = false;
    }
  }

  onPicked(element: SpyElement): void {
    if (element.kind !== 'text-offset') {
      return;
    }
    this.anchorText = element.anchorText ?? '';
    this.dx = element.dx ?? 0;
    this.dy = element.dy ?? 0;
    this.preview = element.imageBase64 ?? null;
    this.emit();
  }

  emit(): void {
    this.valueChange.emit(JSON.stringify({ anchorText: this.anchorText, dx: Math.round(this.dx), dy: Math.round(this.dy) }));
  }

  private parse(json: string): { anchorText: string; dx: number; dy: number } {
    if (!json || json.trim().length === 0) {
      return { anchorText: '', dx: 0, dy: 0 };
    }
    try {
      const p = JSON.parse(json) as Record<string, unknown>;
      return {
        anchorText: typeof p['anchorText'] === 'string' ? (p['anchorText'] as string) : '',
        dx: typeof p['dx'] === 'number' ? (p['dx'] as number) : 0,
        dy: typeof p['dy'] === 'number' ? (p['dy'] as number) : 0,
      };
    } catch {
      return { anchorText: '', dx: 0, dy: 0 };
    }
  }
}
```

`text-offset-editor.component.html`:

```html
<div class="text-offset-editor">
  <button type="button" class="pick-btn" (click)="pick()" [disabled]="picking">
    🎯 {{ 'picker.pickAnchorTarget' | translate }}
  </button>
  <span *ngIf="picking" class="hint">{{ 'picker.picking' | translate }}</span>
  <span *ngIf="error" class="error">{{ error }}</span>

  <label>{{ 'picker.anchorText' | translate }}
    <input type="text" [(ngModel)]="anchorText" (ngModelChange)="emit()" />
  </label>
  <div class="offset-row">
    <label>{{ 'picker.offsetX' | translate }}
      <input type="number" [(ngModel)]="dx" (ngModelChange)="emit()" />
    </label>
    <label>{{ 'picker.offsetY' | translate }}
      <input type="number" [(ngModel)]="dy" (ngModelChange)="emit()" />
    </label>
  </div>
  <img *ngIf="preview" class="preview" [src]="'data:image/png;base64,' + preview" alt="çapa" />
</div>
```

`text-offset-editor.component.scss`:

```scss
.text-offset-editor {
  display: flex;
  flex-direction: column;
  gap: 6px;

  .pick-btn { align-self: flex-start; cursor: pointer; }
  .hint { font-size: 12px; opacity: 0.8; }
  .error { color: #c0392b; font-size: 12px; }
  .offset-row { display: flex; gap: 8px; }
  label { display: flex; flex-direction: column; font-size: 12px; gap: 2px; }
  input[type='number'] { width: 80px; }
  .preview { max-width: 240px; border: 1px solid #ccc; margin-top: 4px; }
}
```

- [ ] **Step 5: Testi çalıştır (PASS)**

Run: `cd src/RPA.Studio && npx jest text-offset-editor`
Expected: PASS (3 test).

- [ ] **Step 6: generic-property dispatch'i bağla**

`generic-property.component.ts`:
- `imports` dizisine `TextOffsetEditorComponent` ekle (import satırı + `@Component.imports`).
- `isSequenceField`'in yanına ekle:
```typescript
  /** Vision.ClickTextOffset gibi çapa+ofset editörü gerektiren alan mı? */
  isTextOffsetField(port: ActivityPort): boolean {
    return port.pickerKind === 'text-offset';
  }
```
- `spyPickerKind` dönüş tipini ve `image-sequence`/`text-offset`'in "spy türü değil editör ipucu" durumunu güncelle:
```typescript
  spyPickerKind(port: ActivityPort): 'sap' | 'web' | 'desktop' | 'image' | null {
    return port.pickerKind === 'image-sequence' || port.pickerKind === 'text-offset'
      ? null
      : (port.pickerKind as 'sap' | 'web' | 'desktop' | 'image' | undefined) ?? null;
  }
```

`generic-property.component.html`'de sequence editör dalının yanına, `isTextOffsetField` için editörü render et (mevcut `app-vision-sequence-editor` bloğunu örnek al):
```html
<app-text-offset-editor
  *ngIf="isTextOffsetField(port)"
  [value]="stringValue(port)"
  (valueChange)="onValueChange(port, $event)"
></app-text-offset-editor>
```
ve bu alan için standart string input'un gösterilmediğinden emin ol (mevcut `isSequenceField` `*ngIf`/`*ngElse` yapısına `isTextOffsetField` koşulunu da ekle — sequence editörüyle aynı şekilde string editörden dışla).

- [ ] **Step 7: i18n anahtarları ekle**

`tr.json` (`picker` bölümüne; yoksa oluştur):
```json
    "picker.pickAnchorTarget": "Çapa ve hedef seç",
    "picker.picking": "Seçim bekleniyor…",
    "picker.anchorText": "Çapa metni",
    "picker.offsetX": "Ofset X (px)",
    "picker.offsetY": "Ofset Y (px)"
```
Varsa `en.json`'a İngilizce karşılıkları ekle (aynı anahtarlar).

- [ ] **Step 8: Studio derleme + testleri çalıştır**

Run: `cd src/RPA.Studio && npx jest text-offset-editor generic-property spy.service && npm run build`
Expected: PASS + derleme başarılı.

- [ ] **Step 9: Commit**

```bash
git add src/RPA.Studio
git commit -m "feat(studio): text-offset editoru + spy kind + i18n

Vision.ClickTextOffset icin capa+ofset editoru; 🎯 iki asamali picker; dx/dy elle duzeltilebilir.

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

## Task 8: Uçtan uca doğrulama (regresyon)

**Files:** yok (yalnız çalıştırma).

- [ ] **Step 1: Tüm backend testleri**

Run: `dotnet test`
Expected: PASS (Domain, Infrastructure, Agent, WebAPI).

- [ ] **Step 2: Studio testleri**

Run: `cd src/RPA.Studio && npx jest`
Expected: PASS.

- [ ] **Step 3: (Manuel — kullanıcı) gerçek ekran testi**

Ajanı Windows'ta çalıştır, Studio'da `Vision.ClickTextOffset` node'u ekle, 🎯 ile bir etiketi çapa seç + yanındaki boş alanı hedefle, workflow'u koştur. `anchorText/dx/dy` doğru mu, tıklama doğru noktaya mı gidiyor doğrula.

> Not: Bu adım gerçek SAP/masaüstü ekranı gerektirir; kullanıcı test edecek. Sonrasında ofset referansı (merkez vs sol-orta) veya çok-kelimeli çapa davranışı için değişiklik gelebilir (spec §9 riskleri).

---

## Self-Review Notları

- **Spec kapsamı:** §3 aktivite (Task 2), §4 kanal (Task 1+3), §5 picker (Task 4+5), §6 Studio editör (Task 7), §7 kayıt (Task 2+5), §8 testler (her task). Tümü karşılandı.
- **Referans tutarlılığı (§3):** picker (Task 5, `OcrEngine.Read` tight kutu) ve runtime (Task 3, `PollForTextAsync` tight kutu) aynı `VisionOffset.ClickPoint` merkez referansını kullanır. ✓
- **Tip tutarlılığı:** `TextOffsetPick(AnchorText, Dx, Dy, PreviewBase64)`, `SpyElementMessage.FromTextOffset(anchorText, dx, dy, previewBase64, sessionId)`, `TextOffsetSpec(AnchorText, Dx, Dy)` — Task 2/4/5 arasında ad/sıra tutarlı. ✓
- **Kapsam dışı:** görüntü-çapa, en-yakın-tekrar ayırt etme, çok-kelime öbek — bilinçli hariç (spec §2, §9).

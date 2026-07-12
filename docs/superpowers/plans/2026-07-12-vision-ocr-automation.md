# Görüntü/OCR Fallback Otomasyonu (Paket F — Vision.*) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Erişilebilirlik ağacı (UIA/DOM) olmayan uygulamalar için template matching + OCR tabanlı fallback otomasyon kanalı (`Vision.*`) eklemek.

**Architecture:** `Desktop.*` (Paket E) deseninin birebir ikizi. Yeni `IVisionAutomationChannel` domain arayüzü; platform-nötr `Vision.*` aktiviteleri Infrastructure'da (mock'lanabilir); gerçek OpenCvSharp+Tesseract implementasyonu Agent'ta (net10.0-windows). 🎯 picker'a `kind:"image"` bölge-seçim modu eklenir; seçilen bölge base64 PNG olarak workflow'a gömülür.

**Tech Stack:** C# / .NET 10, OpenCvSharp4 (+ runtime.win) template matching, Tesseract NuGet + .traineddata OCR, xUnit + Moq, SignalR (mevcut UI Spy transport).

## Global Constraints

- **Onion bağımlılık:** Domain harici bağımlılık YOK; Infrastructure aktiviteleri yalnız arayüze bağlı (mock'lanabilir); OpenCvSharp/Tesseract yalnız Agent (net10.0-windows) projesinde.
- **Exception sınıflandırması:** Parametre doğrulama → `RPA.Domain.Exceptions.BusinessException`. Görüntü/metin bulunamadı & timeout → `System` (Desktop.WaitFor deseni: `ExceptionClassification("Timeout", ExceptionType.System)`). `Exists`/`TextExists` HİÇ fırlatmaz.
- **Naming:** Activity ID dot notation `Vision.*`; capability `vision`; kategori sabiti `CatVision = "Görüntü"`.
- **Çok dil:** OCR aktivitelerinde `language` parametresi `tur+eng+deu` çoklu değer alır; boşsa varsayılan `"tur+eng"`.
- **Görüntü taşıma:** `image` parametresi base64 PNG (gömülü); `PickerKind = "image"`. Ayrı dosya YOK.
- **İleriye hazırlık:** Matcher/OCR iç API'si `IReadOnlyList<VisionMatch>` döndürür (anchor Faz 2 için); aktiviteler en yüksek skorlu tekini kullanır.
- **TDD:** her task failing test → minimal impl → pass → commit.

---

### Task 1: Domain — IVisionAutomationChannel arayüzü + VisionMatch value object

**Files:**
- Create: `src/RPA.Domain/ValueObjects/VisionMatch.cs`
- Create: `src/RPA.Domain/Interfaces/IVisionAutomationChannel.cs`
- Test: `tests/RPA.Domain.Tests/ValueObjects/VisionMatchTests.cs`

**Interfaces:**
- Produces: `RPA.Domain.ValueObjects.VisionMatch` (record: `int X, int Y, int Width, int Height, double Score`; hesaplanan `int CenterX => X + Width / 2;`, `int CenterY => Y + Height / 2;`). `RPA.Domain.Interfaces.IVisionAutomationChannel` (aşağıdaki imzalar).

- [ ] **Step 1: VisionMatch için failing test yaz**

`tests/RPA.Domain.Tests/ValueObjects/VisionMatchTests.cs`:
```csharp
namespace RPA.Domain.Tests.ValueObjects;

using RPA.Domain.ValueObjects;
using Xunit;

public class VisionMatchTests
{
    [Fact]
    public void Center_IsComputedFromBoundingBox()
    {
        var match = new VisionMatch(X: 100, Y: 200, Width: 40, Height: 20, Score: 0.95);

        Assert.Equal(120, match.CenterX);
        Assert.Equal(210, match.CenterY);
        Assert.Equal(0.95, match.Score);
    }
}
```

- [ ] **Step 2: Testi çalıştır — FAIL (tip yok)**

Run: `dotnet test tests/RPA.Domain.Tests --filter FullyQualifiedName~VisionMatchTests`
Expected: FAIL — `VisionMatch` bulunamadı / derlenmez.

- [ ] **Step 3: VisionMatch value object'i yaz**

`src/RPA.Domain/ValueObjects/VisionMatch.cs`:
```csharp
namespace RPA.Domain.ValueObjects;

/// <summary>
/// Ekranda bulunan bir görüntü/metin eşleşmesinin sınır kutusu ve güven skoru.
/// Anchor (Faz 2) için matcher/OCR birden çok VisionMatch döndürür; aktiviteler
/// şimdilik en yüksek skorlu tekini kullanır.
/// </summary>
public sealed record VisionMatch(int X, int Y, int Width, int Height, double Score)
{
    /// <summary>Eşleşmenin yatay merkezi (tıklama noktası).</summary>
    public int CenterX => X + (Width / 2);

    /// <summary>Eşleşmenin dikey merkezi (tıklama noktası).</summary>
    public int CenterY => Y + (Height / 2);
}
```

- [ ] **Step 4: IVisionAutomationChannel arayüzünü yaz**

`src/RPA.Domain/Interfaces/IVisionAutomationChannel.cs`:
```csharp
namespace RPA.Domain.Interfaces;

/// <summary>
/// Erişilebilirlik ağacı (UIA/DOM) olmayan uygulamalar için piksel + metin tabanlı
/// otomasyon kanalı (Spec Bölüm — Paket F, Görüntü/OCR Fallback). Template matching
/// (OpenCvSharp) ve OCR (Tesseract) ile ekrandaki nesneyi bulur; gerçek fare/klavye
/// ile etkileşir. Etkileşimli masaüstü oturumu gerektirir.
///
/// <para>Exception sınıflandırması: görüntü/metin bulunamadı / timeout →
/// <c>SystemException</c> (teknik, retry edilebilir). Var-mı sorguları (<see cref="ImageExistsAsync"/>,
/// <see cref="TextExistsAsync"/>) fırlatmaz, false döner.</para>
/// </summary>
public interface IVisionAutomationChannel
{
    /// <summary>Base64 PNG template'i ekranda bulur, merkezine tıklar. timeoutMs içinde bulunmazsa SystemException.</summary>
    Task ClickImageAsync(string imageBase64, double confidence, string? clickType, int timeoutMs);

    /// <summary>Template ekranda görünene kadar bekler. Süre aşımı → SystemException.</summary>
    Task WaitForImageAsync(string imageBase64, double confidence, int timeoutMs);

    /// <summary>Template ekranda var mı? Fırlatmaz. timeoutMs 0 ise tek bakış.</summary>
    Task<bool> ImageExistsAsync(string imageBase64, double confidence, int timeoutMs);

    /// <summary>Bölgeden (null ise tam ekran) OCR ile metin okur. language örn. "tur+eng".</summary>
    Task<string> GetTextAsync(int? x, int? y, int? width, int? height, string language);

    /// <summary>OCR ile metni bulur, merkezine tıklar. matchMode "contains" (vars.) / "exact". Bulunmazsa SystemException.</summary>
    Task ClickTextAsync(string text, string language, string matchMode, string? clickType, int timeoutMs);

    /// <summary>Metin ekranda var mı? Fırlatmaz.</summary>
    Task<bool> TextExistsAsync(string text, string language, string matchMode, int timeoutMs);
}
```

- [ ] **Step 5: Testi çalıştır — PASS**

Run: `dotnet test tests/RPA.Domain.Tests --filter FullyQualifiedName~VisionMatchTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/RPA.Domain/ValueObjects/VisionMatch.cs src/RPA.Domain/Interfaces/IVisionAutomationChannel.cs tests/RPA.Domain.Tests/ValueObjects/VisionMatchTests.cs
git commit -m "feat(domain): IVisionAutomationChannel + VisionMatch (Paket F)

Görüntü/OCR fallback otomasyon kanalı sözleşmesi ve eşleşme value object'i.

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 2: Infrastructure — Vision.* aktiviteleri + Unavailable fallback

**Files:**
- Create: `src/RPA.Infrastructure/Activities/Vision/VisionActivities.cs`
- Create: `src/RPA.Infrastructure/Activities/Vision/UnavailableVisionAutomationChannel.cs`
- Test: `tests/RPA.Infrastructure.Tests/Activities/VisionActivitiesTests.cs`

**Interfaces:**
- Consumes: `IVisionAutomationChannel` (Task 1); `RPA.Domain.Interfaces.IActivity`, `IActivityExecutionContext`, `ActivityMetadata`, `ActivityParameter`, `ExceptionClassificationRule`; `RPA.Domain.Exceptions.BusinessException`; `RPA.Domain.Enums.ExceptionType`.
- Produces: `VisionClickActivity`, `VisionWaitForActivity`, `VisionExistsActivity`, `VisionGetTextActivity`, `VisionClickTextActivity`, `VisionTextExistsActivity` (hepsi `IActivity`, ctor `(IVisionAutomationChannel channel)`). `UnavailableVisionAutomationChannel : IVisionAutomationChannel`.

> **Not:** `IActivityExecutionContext` API'si (`GetVariable<T>(name)`, `SetVariable(name, value)`, `Log(msg)`) mevcut `Desktop/DesktopActivities.cs` ile birebir aynıdır — o dosyayı referans al.

- [ ] **Step 1: Failing test yaz (parametre doğrulama + çıktı + Exists fırlatmama)**

`tests/RPA.Infrastructure.Tests/Activities/VisionActivitiesTests.cs`:
```csharp
namespace RPA.Infrastructure.Tests.Activities;

using Moq;
using RPA.Domain.Exceptions;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Activities.Vision;
using Xunit;

public class VisionActivitiesTests
{
    private static Mock<IActivityExecutionContext> Ctx(Dictionary<string, object?> vars)
    {
        var ctx = new Mock<IActivityExecutionContext>();
        ctx.Setup(c => c.GetVariable<string?>(It.IsAny<string>()))
           .Returns((string n) => vars.TryGetValue(n, out var v) ? (string?)v : null);
        ctx.Setup(c => c.GetVariable<double>(It.IsAny<string>()))
           .Returns((string n) => vars.TryGetValue(n, out var v) && v is double d ? d : 0d);
        ctx.Setup(c => c.GetVariable<int>(It.IsAny<string>()))
           .Returns((string n) => vars.TryGetValue(n, out var v) && v is int i ? i : 0);
        return ctx;
    }

    [Fact]
    public async Task Click_EmptyImage_ThrowsBusiness()
    {
        var channel = new Mock<IVisionAutomationChannel>();
        var activity = new VisionClickActivity(channel.Object);
        var ctx = Ctx(new() { ["image"] = "" });

        await Assert.ThrowsAsync<BusinessException>(() => activity.ExecuteAsync(ctx.Object));
    }

    [Fact]
    public async Task Click_ValidImage_CallsChannelWithDefaults()
    {
        var channel = new Mock<IVisionAutomationChannel>();
        var activity = new VisionClickActivity(channel.Object);
        var ctx = Ctx(new() { ["image"] = "BASE64", ["confidence"] = 0d, ["timeoutMs"] = 0 });

        await activity.ExecuteAsync(ctx.Object);

        // confidence 0 → varsayılan 0.8, clickType null → "left" kanala bırakılır
        channel.Verify(c => c.ClickImageAsync("BASE64", 0.8, null, 0), Times.Once);
    }

    [Fact]
    public async Task Exists_NotFound_ReturnsFalse_NoThrow()
    {
        var channel = new Mock<IVisionAutomationChannel>();
        channel.Setup(c => c.ImageExistsAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>()))
               .ReturnsAsync(false);
        var activity = new VisionExistsActivity(channel.Object);
        var ctx = Ctx(new() { ["image"] = "BASE64" });

        var result = await activity.ExecuteAsync(ctx.Object);

        Assert.Equal(false, result["exists"]);
    }

    [Fact]
    public async Task GetText_DefaultLanguage_IsTurEng()
    {
        var channel = new Mock<IVisionAutomationChannel>();
        channel.Setup(c => c.GetTextAsync(null, null, null, null, "tur+eng")).ReturnsAsync("okunan");
        var activity = new VisionGetTextActivity(channel.Object);
        var ctx = Ctx(new());

        var result = await activity.ExecuteAsync(ctx.Object);

        Assert.Equal("okunan", result["text"]);
        channel.Verify(c => c.GetTextAsync(null, null, null, null, "tur+eng"), Times.Once);
    }

    [Fact]
    public async Task ClickText_EmptyText_ThrowsBusiness()
    {
        var channel = new Mock<IVisionAutomationChannel>();
        var activity = new VisionClickTextActivity(channel.Object);
        var ctx = Ctx(new() { ["text"] = "  " });

        await Assert.ThrowsAsync<BusinessException>(() => activity.ExecuteAsync(ctx.Object));
    }
}
```

- [ ] **Step 2: Testi çalıştır — FAIL (aktiviteler yok)**

Run: `dotnet test tests/RPA.Infrastructure.Tests --filter FullyQualifiedName~VisionActivitiesTests`
Expected: FAIL — derlenmez (`VisionClickActivity` vb. yok).

- [ ] **Step 3: UnavailableVisionAutomationChannel yaz**

`src/RPA.Infrastructure/Activities/Vision/UnavailableVisionAutomationChannel.cs`:
```csharp
namespace RPA.Infrastructure.Activities.Vision;

using RPA.Domain.Interfaces;

/// <summary>
/// Non-agent süreçlerde Vision.* aktivite kayıtlarının DI-geçerli kalması için yer tutucu kanal.
/// Gerçek OpenCvSharp+Tesseract implementasyonu RPA.Agent tarafından Windows'ta kaydedilir.
/// </summary>
public sealed class UnavailableVisionAutomationChannel : IVisionAutomationChannel
{
    public Task ClickImageAsync(string imageBase64, double confidence, string? clickType, int timeoutMs) => Unavailable();
    public Task WaitForImageAsync(string imageBase64, double confidence, int timeoutMs) => Unavailable();
    public Task<bool> ImageExistsAsync(string imageBase64, double confidence, int timeoutMs) => Unavailable<bool>();
    public Task<string> GetTextAsync(int? x, int? y, int? width, int? height, string language) => Unavailable<string>();
    public Task ClickTextAsync(string text, string language, string matchMode, string? clickType, int timeoutMs) => Unavailable();
    public Task<bool> TextExistsAsync(string text, string language, string matchMode, int timeoutMs) => Unavailable<bool>();

    private static Task Unavailable() => Task.FromException(new InvalidOperationException(Message));
    private static Task<T> Unavailable<T>() => Task.FromException<T>(new InvalidOperationException(Message));

    private const string Message =
        "Vision automation channel is not available in this process. Run Vision.* activities through RPA.Agent on Windows.";
}
```

- [ ] **Step 4: VisionActivities.cs yaz (6 aktivite + meta yardımcıları)**

`src/RPA.Infrastructure/Activities/Vision/VisionActivities.cs`:
```csharp
namespace RPA.Infrastructure.Activities.Vision;

using RPA.Domain.Interfaces;
using BusinessException = RPA.Domain.Exceptions.BusinessException;

// Görüntü/OCR Fallback Otomasyonu — Vision.* aktivite ailesi (Paket F).
// Her aktivite IVisionAutomationChannel üzerinden çalışır (OpenCvSharp+Tesseract impl.
// RPA.Agent'ta yaşar). Platform-nötr; yalnız arayüzü çağırır → testlerde mock'lanır.
// Parametre doğrulama → BusinessException. Bulunamadı/timeout kanalda SystemException.

internal static class VisionMeta
{
    public const string Category = "Görüntü";
    public const string Capability = "vision";
    public const string DefaultLanguage = "tur+eng";
    public const double DefaultConfidence = 0.8;

    public static ActivityParameter Image() => new()
    {
        Name = "image", Type = "string", Required = true,
        Description = "Ekranda aranacak görüntü (base64 PNG). 🎯 ile bölge seçin.",
        PickerKind = "image",
    };

    public static ActivityParameter Confidence() => new()
    {
        Name = "confidence", Type = "double", Required = false,
        Description = "Eşleşme güven eşiği (0-1).", DefaultValue = DefaultConfidence,
    };

    public static ActivityParameter Language() => new()
    {
        Name = "language", Type = "string", Required = false,
        Description = "OCR dil(ler)i, örn. 'tur+eng+deu'.", DefaultValue = DefaultLanguage,
    };

    public static ActivityParameter MatchMode() => new()
    {
        Name = "matchMode", Type = "string", Required = false,
        Description = "Metin eşleşme kipi.", Options = new() { "contains", "exact" }, DefaultValue = "contains",
    };

    public static ActivityParameter ClickType() => new()
    {
        Name = "clickType", Type = "string", Required = false,
        Description = "Tıklama türü.", Options = new() { "left", "right", "double" }, DefaultValue = "left",
    };

    public static string RequireImage(IActivityExecutionContext context)
    {
        var image = context.GetVariable<string?>("image");
        if (string.IsNullOrWhiteSpace(image))
        {
            throw new BusinessException("'image' parametresi boş olamaz.");
        }
        return image;
    }

    public static string RequireText(IActivityExecutionContext context)
    {
        var text = context.GetVariable<string?>("text");
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new BusinessException("'text' parametresi boş olamaz.");
        }
        return text;
    }

    public static double ConfidenceOrDefault(IActivityExecutionContext context)
    {
        var c = context.GetVariable<double>("confidence");
        return c is > 0 and <= 1 ? c : DefaultConfidence;
    }

    public static string LanguageOrDefault(IActivityExecutionContext context)
    {
        var lang = context.GetVariable<string?>("language");
        return string.IsNullOrWhiteSpace(lang) ? DefaultLanguage : lang;
    }

    public static string MatchModeOrDefault(IActivityExecutionContext context)
    {
        var m = context.GetVariable<string?>("matchMode");
        return string.Equals(m, "exact", StringComparison.OrdinalIgnoreCase) ? "exact" : "contains";
    }

    public static string? ClickTypeOrNull(IActivityExecutionContext context)
    {
        var t = context.GetVariable<string?>("clickType");
        return string.IsNullOrWhiteSpace(t) ? null : t;
    }
}

/// <summary>Ekranda görüntüyü bulur ve merkezine tıklar.</summary>
public sealed class VisionClickActivity : IActivity
{
    private readonly IVisionAutomationChannel _channel;
    public VisionClickActivity(IVisionAutomationChannel channel)
        => _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public ActivityMetadata GetMetadata() => new()
    {
        ActivityId = "Vision.Click",
        DisplayName = "Görüntüye Tıkla",
        Category = VisionMeta.Category,
        Description = "Ekranda bir görüntüyü bulur ve merkezine tıklar (erişilemeyen uygulamalar).",
        Inputs = new()
        {
            VisionMeta.Image(),
            VisionMeta.Confidence(),
            VisionMeta.ClickType(),
            new ActivityParameter { Name = "timeoutMs", Type = "int", Required = false, Description = "Zaman aşımı (ms).", DefaultValue = 5000 },
        },
        Outputs = new(),
        RequiredCapabilities = new() { VisionMeta.Capability },
        ExceptionClassification = new ExceptionClassificationRule { Condition = "Timeout", Classification = RPA.Domain.Enums.ExceptionType.System },
    };

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var image = VisionMeta.RequireImage(context);
        var confidence = VisionMeta.ConfidenceOrDefault(context);
        var clickType = VisionMeta.ClickTypeOrNull(context);
        var timeoutMs = context.GetVariable<int>("timeoutMs");
        context.Log($"Görüntüye tıklanıyor (confidence {confidence}).");
        await _channel.ClickImageAsync(image, confidence, clickType, timeoutMs);
        return new();
    }
}

/// <summary>Görüntü görünene kadar bekler.</summary>
public sealed class VisionWaitForActivity : IActivity
{
    private readonly IVisionAutomationChannel _channel;
    public VisionWaitForActivity(IVisionAutomationChannel channel)
        => _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public ActivityMetadata GetMetadata() => new()
    {
        ActivityId = "Vision.WaitFor",
        DisplayName = "Görüntü Bekle",
        Category = VisionMeta.Category,
        Description = "Bir görüntü ekranda görünene kadar bekler (timeout → System).",
        Inputs = new()
        {
            VisionMeta.Image(),
            VisionMeta.Confidence(),
            new ActivityParameter { Name = "timeoutMs", Type = "int", Required = false, Description = "Zaman aşımı (ms).", DefaultValue = 10000 },
        },
        Outputs = new(),
        RequiredCapabilities = new() { VisionMeta.Capability },
        ExceptionClassification = new ExceptionClassificationRule { Condition = "Timeout", Classification = RPA.Domain.Enums.ExceptionType.System },
    };

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var image = VisionMeta.RequireImage(context);
        var confidence = VisionMeta.ConfidenceOrDefault(context);
        var timeoutMs = context.GetVariable<int>("timeoutMs");
        context.Log("Görüntü bekleniyor.");
        await _channel.WaitForImageAsync(image, confidence, timeoutMs);
        return new();
    }
}

/// <summary>Görüntü ekranda var mı? (fırlatmaz)</summary>
public sealed class VisionExistsActivity : IActivity
{
    private readonly IVisionAutomationChannel _channel;
    public VisionExistsActivity(IVisionAutomationChannel channel)
        => _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public ActivityMetadata GetMetadata() => new()
    {
        ActivityId = "Vision.Exists",
        DisplayName = "Görüntü Var mı?",
        Category = VisionMeta.Category,
        Description = "Görüntü ekranda var mı kontrol eder; 'exists' (bool) döner, hata fırlatmaz.",
        Inputs = new()
        {
            VisionMeta.Image(),
            VisionMeta.Confidence(),
            new ActivityParameter { Name = "timeoutMs", Type = "int", Required = false, Description = "Zaman aşımı (ms). 0 = tek bakış.", DefaultValue = 0 },
        },
        Outputs = new() { new ActivityParameter { Name = "exists", Type = "bool", Required = false, Description = "Görüntü bulundu mu." } },
        RequiredCapabilities = new() { VisionMeta.Capability },
    };

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var image = VisionMeta.RequireImage(context);
        var confidence = VisionMeta.ConfidenceOrDefault(context);
        var timeoutMs = context.GetVariable<int>("timeoutMs");
        var exists = await _channel.ImageExistsAsync(image, confidence, timeoutMs);
        context.SetVariable("exists", exists);
        return new() { ["exists"] = exists };
    }
}

/// <summary>Bölgeden OCR ile metin okur.</summary>
public sealed class VisionGetTextActivity : IActivity
{
    private readonly IVisionAutomationChannel _channel;
    public VisionGetTextActivity(IVisionAutomationChannel channel)
        => _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public ActivityMetadata GetMetadata() => new()
    {
        ActivityId = "Vision.GetText",
        DisplayName = "Görüntüden Metin Oku (OCR)",
        Category = VisionMeta.Category,
        Description = "Bir ekran bölgesinden (boşsa tam ekran) OCR ile metin okur.",
        Inputs = new()
        {
            new ActivityParameter { Name = "region", Type = "string", Required = false, Description = "Bölge {x,y,width,height} (boşsa tam ekran). 🎯 ile seçin.", PickerKind = "image" },
            VisionMeta.Language(),
        },
        Outputs = new() { new ActivityParameter { Name = "text", Type = "string", Required = false, Description = "Okunan metin." } },
        RequiredCapabilities = new() { VisionMeta.Capability },
    };

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var region = VisionRegion.Parse(context.GetVariable<string?>("region"));
        var language = VisionMeta.LanguageOrDefault(context);
        context.Log($"OCR ile metin okunuyor (dil {language}).");
        var text = await _channel.GetTextAsync(region.X, region.Y, region.Width, region.Height, language);
        context.SetVariable("text", text);
        return new() { ["text"] = text };
    }
}

/// <summary>OCR ile metni bulur ve tıklar.</summary>
public sealed class VisionClickTextActivity : IActivity
{
    private readonly IVisionAutomationChannel _channel;
    public VisionClickTextActivity(IVisionAutomationChannel channel)
        => _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public ActivityMetadata GetMetadata() => new()
    {
        ActivityId = "Vision.ClickText",
        DisplayName = "Metne Tıkla (OCR)",
        Category = VisionMeta.Category,
        Description = "OCR ile bir metni ekranda bulur ve üstüne tıklar (timeout → System).",
        Inputs = new()
        {
            new ActivityParameter { Name = "text", Type = "string", Required = true, Description = "Aranacak metin." },
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
        var text = VisionMeta.RequireText(context);
        var language = VisionMeta.LanguageOrDefault(context);
        var matchMode = VisionMeta.MatchModeOrDefault(context);
        var clickType = VisionMeta.ClickTypeOrNull(context);
        var timeoutMs = context.GetVariable<int>("timeoutMs");
        context.Log($"Metne tıklanıyor: '{text}' (dil {language}).");
        await _channel.ClickTextAsync(text, language, matchMode, clickType, timeoutMs);
        return new();
    }
}

/// <summary>Metin ekranda var mı? (fırlatmaz)</summary>
public sealed class VisionTextExistsActivity : IActivity
{
    private readonly IVisionAutomationChannel _channel;
    public VisionTextExistsActivity(IVisionAutomationChannel channel)
        => _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public ActivityMetadata GetMetadata() => new()
    {
        ActivityId = "Vision.TextExists",
        DisplayName = "Metin Var mı? (OCR)",
        Category = VisionMeta.Category,
        Description = "Metin ekranda var mı kontrol eder; 'exists' (bool) döner, hata fırlatmaz.",
        Inputs = new()
        {
            new ActivityParameter { Name = "text", Type = "string", Required = true, Description = "Aranacak metin." },
            VisionMeta.Language(),
            VisionMeta.MatchMode(),
            new ActivityParameter { Name = "timeoutMs", Type = "int", Required = false, Description = "Zaman aşımı (ms). 0 = tek bakış.", DefaultValue = 0 },
        },
        Outputs = new() { new ActivityParameter { Name = "exists", Type = "bool", Required = false, Description = "Metin bulundu mu." } },
        RequiredCapabilities = new() { VisionMeta.Capability },
    };

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var text = VisionMeta.RequireText(context);
        var language = VisionMeta.LanguageOrDefault(context);
        var matchMode = VisionMeta.MatchModeOrDefault(context);
        var timeoutMs = context.GetVariable<int>("timeoutMs");
        var exists = await _channel.TextExistsAsync(text, language, matchMode, timeoutMs);
        context.SetVariable("exists", exists);
        return new() { ["exists"] = exists };
    }
}

/// <summary>region parametresini {x,y,width,height} JSON'undan çözer; boşsa null bileşenler (tam ekran).</summary>
internal readonly record struct VisionRegion(int? X, int? Y, int? Width, int? Height)
{
    public static VisionRegion Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new VisionRegion(null, null, null, null);
        }
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var r = doc.RootElement;
            return new VisionRegion(
                r.TryGetProperty("x", out var x) ? x.GetInt32() : null,
                r.TryGetProperty("y", out var y) ? y.GetInt32() : null,
                r.TryGetProperty("width", out var w) ? w.GetInt32() : null,
                r.TryGetProperty("height", out var h) ? h.GetInt32() : null);
        }
        catch (System.Text.Json.JsonException)
        {
            return new VisionRegion(null, null, null, null);
        }
    }
}
```

- [ ] **Step 5: Testi çalıştır — PASS**

Run: `dotnet test tests/RPA.Infrastructure.Tests --filter FullyQualifiedName~VisionActivitiesTests`
Expected: PASS (5 test).

> Not: `ActivityParameter`'da `DefaultValue`, `Options`, `PickerKind`, `Type="double"` alanlarının var olduğunu doğrula (Desktop aktivitelerinde kullanılıyor). Yoksa mevcut kullanımı taban al.

- [ ] **Step 6: Commit**

```bash
git add src/RPA.Infrastructure/Activities/Vision/ tests/RPA.Infrastructure.Tests/Activities/VisionActivitiesTests.cs
git commit -m "feat(infrastructure): Vision.* aktiviteleri (Paket F)

Click/WaitFor/Exists/GetText/ClickText/TextExists + Unavailable fallback.
Parametre doğrulama Business; bulunamadı/timeout kanalda System.

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 3: Katalog kaydı + DI (RegisterVision + keyed IActivity + Unavailable kanal)

**Files:**
- Modify: `src/RPA.Infrastructure/Workflow/ActivityRegistry.cs` (yeni `RegisterVision` + `CatVision` sabiti + `RegisterVision(b)` çağrısı)
- Modify: `src/RPA.Infrastructure/Workflow/WorkflowServiceCollectionExtensions.cs` (6 keyed `IActivity` + `IVisionAutomationChannel` → `UnavailableVisionAutomationChannel` TryAddSingleton)
- Test: `tests/RPA.Infrastructure.Tests/Workflow/VisionCatalogTests.cs`

**Interfaces:**
- Consumes: Task 2 aktiviteleri; `ActivityCatalogBuilder` fluent API (`Activity(id).DisplayName().Category().Capability().Description().Input().Output().ExceptionClassification()` — mevcut RegisterDesktop deseni).
- Produces: katalogda 6 `Vision.*` girişi; DI'da 6 keyed `IActivity`.

- [ ] **Step 1: Failing test yaz (katalog 6 Vision aktivitesini içeriyor)**

`tests/RPA.Infrastructure.Tests/Workflow/VisionCatalogTests.cs`:
```csharp
namespace RPA.Infrastructure.Tests.Workflow;

using System.Linq;
using RPA.Infrastructure.Workflow;
using Xunit;

public class VisionCatalogTests
{
    [Theory]
    [InlineData("Vision.Click")]
    [InlineData("Vision.WaitFor")]
    [InlineData("Vision.Exists")]
    [InlineData("Vision.GetText")]
    [InlineData("Vision.ClickText")]
    [InlineData("Vision.TextExists")]
    public void Catalog_ContainsVisionActivity(string activityId)
    {
        var catalog = ActivityRegistry.BuildCatalog();
        Assert.Contains(catalog, a => a.ActivityId == activityId);
    }

    [Fact]
    public void VisionActivities_HaveVisionCapability()
    {
        var catalog = ActivityRegistry.BuildCatalog();
        var vision = catalog.Where(a => a.ActivityId.StartsWith("Vision.")).ToList();
        Assert.Equal(6, vision.Count);
        Assert.All(vision, a => Assert.Contains("vision", a.RequiredCapabilities));
    }
}
```

> **Not:** Katalog oluşturma metodunun gerçek adını `ActivityRegistry.cs`'te doğrula (`BuildCatalog` veya benzeri). Testte o adı kullan.

- [ ] **Step 2: Testi çalıştır — FAIL**

Run: `dotnet test tests/RPA.Infrastructure.Tests --filter FullyQualifiedName~VisionCatalogTests`
Expected: FAIL — Vision.* katalogda yok.

- [ ] **Step 3: ActivityRegistry'ye RegisterVision ekle**

`ActivityRegistry.cs` — `RegisterDesktop(b);` çağrısının yanına `RegisterVision(b);` ekle. Sınıfta `CatDesktop` yakınına `private const string CatVision = "Görüntü";` ekle. `RegisterDesktop` metodunun altına:
```csharp
    // ---- Görüntü/OCR Fallback (Vision.*) — Paket F ----
    private static void RegisterVision(ActivityCatalogBuilder b)
    {
        const string cap = "vision";

        b.Activity("Vision.Click").DisplayName("Görüntüye Tıkla").Category(CatVision).Capability(cap)
            .Description("Ekranda bir görüntüyü bulur ve merkezine tıklar.")
            .Input("image", "string", pickerKind: "image", description: "Aranacak görüntü (base64 PNG).")
            .Input("confidence", "double", required: false, defaultValue: 0.8)
            .Input("clickType", "string", required: false, defaultValue: "left", options: new[] { "left", "right", "double" })
            .Input("timeoutMs", "int", required: false, defaultValue: 5000)
            .ExceptionClassification("Timeout", ExceptionType.System);

        b.Activity("Vision.WaitFor").DisplayName("Görüntü Bekle").Category(CatVision).Capability(cap)
            .Description("Bir görüntü ekranda görünene kadar bekler (timeout → System).")
            .Input("image", "string", pickerKind: "image")
            .Input("confidence", "double", required: false, defaultValue: 0.8)
            .Input("timeoutMs", "int", required: false, defaultValue: 10000)
            .ExceptionClassification("Timeout", ExceptionType.System);

        b.Activity("Vision.Exists").DisplayName("Görüntü Var mı?").Category(CatVision).Capability(cap)
            .Description("Görüntü ekranda var mı; 'exists' (bool) döner, fırlatmaz.")
            .Input("image", "string", pickerKind: "image")
            .Input("confidence", "double", required: false, defaultValue: 0.8)
            .Input("timeoutMs", "int", required: false, defaultValue: 0)
            .Output("exists", "bool");

        b.Activity("Vision.GetText").DisplayName("Görüntüden Metin Oku (OCR)").Category(CatVision).Capability(cap)
            .Description("Bir ekran bölgesinden (boşsa tam ekran) OCR ile metin okur.")
            .Input("region", "string", required: false, pickerKind: "image", description: "Bölge {x,y,width,height}.")
            .Input("language", "string", required: false, defaultValue: "tur+eng")
            .Output("text", "string");

        b.Activity("Vision.ClickText").DisplayName("Metne Tıkla (OCR)").Category(CatVision).Capability(cap)
            .Description("OCR ile bir metni bulur ve tıklar (timeout → System).")
            .Input("text", "string")
            .Input("language", "string", required: false, defaultValue: "tur+eng")
            .Input("matchMode", "string", required: false, defaultValue: "contains", options: new[] { "contains", "exact" })
            .Input("clickType", "string", required: false, defaultValue: "left", options: new[] { "left", "right", "double" })
            .Input("timeoutMs", "int", required: false, defaultValue: 5000)
            .ExceptionClassification("Timeout", ExceptionType.System);

        b.Activity("Vision.TextExists").DisplayName("Metin Var mı? (OCR)").Category(CatVision).Capability(cap)
            .Description("Metin ekranda var mı; 'exists' (bool) döner, fırlatmaz.")
            .Input("text", "string")
            .Input("language", "string", required: false, defaultValue: "tur+eng")
            .Input("matchMode", "string", required: false, defaultValue: "contains", options: new[] { "contains", "exact" })
            .Input("timeoutMs", "int", required: false, defaultValue: 0)
            .Output("exists", "bool");
    }
```

> **Not:** `.Input(...)` overload'ının `pickerKind:` ve `defaultValue: 0.8` (double) ve `description:` adlandırılmış parametrelerini desteklediğini `ActivityCatalogBuilder`'da doğrula. `double` defaultValue desteklenmiyorsa mevcut builder imzasına uydur (gerekirse `object` defaultValue).

- [ ] **Step 4: DI kaydı ekle (WorkflowServiceCollectionExtensions)**

`WorkflowServiceCollectionExtensions.cs` — Desktop keyed kayıtlarının altına:
```csharp
        services.AddKeyedTransient<IActivity, RPA.Infrastructure.Activities.Vision.VisionClickActivity>("Vision.Click");
        services.AddKeyedTransient<IActivity, RPA.Infrastructure.Activities.Vision.VisionWaitForActivity>("Vision.WaitFor");
        services.AddKeyedTransient<IActivity, RPA.Infrastructure.Activities.Vision.VisionExistsActivity>("Vision.Exists");
        services.AddKeyedTransient<IActivity, RPA.Infrastructure.Activities.Vision.VisionGetTextActivity>("Vision.GetText");
        services.AddKeyedTransient<IActivity, RPA.Infrastructure.Activities.Vision.VisionClickTextActivity>("Vision.ClickText");
        services.AddKeyedTransient<IActivity, RPA.Infrastructure.Activities.Vision.VisionTextExistsActivity>("Vision.TextExists");
```
Ayrıca (Desktop kanalının `UnavailableDesktopAutomationChannel` ile nasıl `TryAddSingleton` edildiğini takip et) aynı yerde:
```csharp
        services.TryAddSingleton<RPA.Domain.Interfaces.IVisionAutomationChannel,
            RPA.Infrastructure.Activities.Vision.UnavailableVisionAutomationChannel>();
```
> `TryAddSingleton` için `using Microsoft.Extensions.DependencyInjection.Extensions;` gerekebilir — dosyanın mevcut `IDesktopAutomationChannel` kaydını referans al ve aynısını uygula.

- [ ] **Step 5: Testi çalıştır — PASS + tüm Infrastructure testleri**

Run: `dotnet test tests/RPA.Infrastructure.Tests --filter FullyQualifiedName~VisionCatalogTests`
Expected: PASS.
Run: `dotnet test tests/RPA.Infrastructure.Tests`
Expected: tüm testler PASS (regresyon yok).

- [ ] **Step 6: Commit**

```bash
git add src/RPA.Infrastructure/Workflow/ActivityRegistry.cs src/RPA.Infrastructure/Workflow/WorkflowServiceCollectionExtensions.cs tests/RPA.Infrastructure.Tests/Workflow/VisionCatalogTests.cs
git commit -m "feat(infrastructure): Vision.* katalog + DI kaydı (Paket F)

RegisterVision (6 aktivite, capability vision) + keyed IActivity +
UnavailableVisionAutomationChannel TryAddSingleton.

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 4: Agent — OpenCvSharp template matcher (golden-file testli)

**Files:**
- Modify: `src/RPA.Agent/RPA.Agent.csproj` (OpenCvSharp4 + OpenCvSharp4.runtime.win + Tesseract PackageReference)
- Create: `src/RPA.Agent/Vision/TemplateMatcher.cs`
- Create: `tests/RPA.Agent.Tests/Vision/TemplateMatcherTests.cs` + gömülü test görüntüleri
- Test: aynı dosya

**Interfaces:**
- Produces: `RPA.Agent.Vision.TemplateMatcher` — statik `public static IReadOnlyList<VisionMatch> FindAll(Mat haystack, Mat needle, double confidence)` ve `public static VisionMatch? FindBest(Mat haystack, Mat needle, double confidence)`. Anchor Faz 2 için `FindAll` çok eşleşme döndürür.

- [ ] **Step 1: NuGet paketlerini ekle**

`RPA.Agent.csproj` `<ItemGroup>` içine:
```xml
    <PackageReference Include="OpenCvSharp4" Version="4.10.0.20241108" />
    <PackageReference Include="OpenCvSharp4.runtime.win" Version="4.10.0.20241108" />
    <PackageReference Include="Tesseract" Version="5.2.0" />
```
Run: `dotnet restore src/RPA.Agent/RPA.Agent.csproj`
Expected: restore başarılı.

- [ ] **Step 2: Failing test yaz (bilinen görüntüde alt-görüntü bulma)**

`tests/RPA.Agent.Tests/Vision/TemplateMatcherTests.cs`:
```csharp
namespace RPA.Agent.Tests.Vision;

using OpenCvSharp;
using RPA.Agent.Vision;
using Xunit;

public class TemplateMatcherTests
{
    // 100x100 beyaz zemin, (40,30) konumunda 10x10 siyah kare içeren haystack üret.
    private static Mat MakeHaystack(out Rect knownBox)
    {
        var img = new Mat(new Size(100, 100), MatType.CV_8UC3, Scalar.White);
        knownBox = new Rect(40, 30, 10, 10);
        Cv2.Rectangle(img, knownBox, Scalar.Black, thickness: -1);
        return img;
    }

    private static Mat MakeNeedle()
    {
        // 10x10 siyah kare — haystack'teki desenle aynı.
        return new Mat(new Size(10, 10), MatType.CV_8UC3, Scalar.Black);
    }

    [Fact]
    public void FindBest_LocatesNeedle_AtKnownPosition()
    {
        using var haystack = MakeHaystack(out var box);
        using var needle = MakeNeedle();

        var match = TemplateMatcher.FindBest(haystack, needle, confidence: 0.8);

        Assert.NotNull(match);
        Assert.InRange(match!.X, box.X - 2, box.X + 2);
        Assert.InRange(match.Y, box.Y - 2, box.Y + 2);
        Assert.True(match.Score >= 0.8);
    }

    [Fact]
    public void FindBest_ReturnsNull_WhenBelowConfidence()
    {
        using var haystack = new Mat(new Size(100, 100), MatType.CV_8UC3, Scalar.White);
        using var needle = MakeNeedle(); // siyah kare beyaz zeminde yok

        var match = TemplateMatcher.FindBest(haystack, needle, confidence: 0.95);

        Assert.Null(match);
    }
}
```

- [ ] **Step 3: Testi çalıştır — FAIL**

Run: `dotnet test tests/RPA.Agent.Tests --filter FullyQualifiedName~TemplateMatcherTests`
Expected: FAIL — `TemplateMatcher` yok.

- [ ] **Step 4: TemplateMatcher'ı yaz**

`src/RPA.Agent/Vision/TemplateMatcher.cs`:
```csharp
namespace RPA.Agent.Vision;

using System.Runtime.Versioning;
using OpenCvSharp;
using RPA.Domain.ValueObjects;

/// <summary>
/// OpenCvSharp tabanlı template matching. Anchor (Faz 2) için FindAll birden çok eşleşme
/// döndürür; şu an aktiviteler yalnız FindBest'i kullanır. Çok-ölçekli tarama DPI toleransı
/// sağlar.
/// </summary>
[SupportedOSPlatform("windows")]
public static class TemplateMatcher
{
    private static readonly double[] Scales = { 1.0, 0.9, 1.1, 0.8, 1.25 };

    public static VisionMatch? FindBest(Mat haystack, Mat needle, double confidence)
    {
        VisionMatch? best = null;
        foreach (var scale in Scales)
        {
            using var scaled = scale == 1.0
                ? needle.Clone()
                : needle.Resize(default, scale, scale, InterpolationFlags.Area);
            if (scaled.Width > haystack.Width || scaled.Height > haystack.Height)
            {
                continue;
            }

            using var result = new Mat();
            Cv2.MatchTemplate(haystack, scaled, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out Point maxLoc);
            if (maxVal >= confidence && (best is null || maxVal > best.Score))
            {
                best = new VisionMatch(maxLoc.X, maxLoc.Y, scaled.Width, scaled.Height, maxVal);
            }
        }
        return best;
    }

    public static IReadOnlyList<VisionMatch> FindAll(Mat haystack, Mat needle, double confidence)
    {
        // Faz 1: tek en iyi eşleşmeyi liste olarak döndür (anchor Faz 2'de genişletilecek).
        var best = FindBest(haystack, needle, confidence);
        return best is null ? Array.Empty<VisionMatch>() : new[] { best };
    }
}
```

- [ ] **Step 5: Testi çalıştır — PASS**

Run: `dotnet test tests/RPA.Agent.Tests --filter FullyQualifiedName~TemplateMatcherTests`
Expected: PASS (2 test).

- [ ] **Step 6: Commit**

```bash
git add src/RPA.Agent/RPA.Agent.csproj src/RPA.Agent/Vision/TemplateMatcher.cs tests/RPA.Agent.Tests/Vision/TemplateMatcherTests.cs
git commit -m "feat(agent): OpenCvSharp template matcher (Paket F)

Çok-ölçekli MatchTemplate; FindBest/FindAll (anchor Faz 2 için çok-eşleşme hazır).

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 5: Agent — TesseractOpenCvVisionChannel (ekran yakalama + OCR + input)

**Files:**
- Create: `src/RPA.Agent/Vision/ScreenCapture.cs` (GDI tam-ekran/bölge yakalama → Mat)
- Create: `src/RPA.Agent/Vision/TesseractOpenCvVisionChannel.cs`
- Create: `tests/RPA.Agent.Tests/Vision/OcrTextMatchTests.cs`
- Modify: `src/RPA.Agent/RPA.Agent.csproj` (tessdata .traineddata dosyalarını `CopyToOutputDirectory`)

**Interfaces:**
- Consumes: `TemplateMatcher` (Task 4); `IVisionAutomationChannel` (Task 1); `VisionMatch`.
- Produces: `RPA.Agent.Vision.TesseractOpenCvVisionChannel : IVisionAutomationChannel`. Ayrıca statik test edilebilir yardımcı `RPA.Agent.Vision.OcrTextMatch.Matches(string ocrWord, string query, string matchMode)` (bool).

> **Not:** Ekran yakalama, gerçek fare (`Cv2` değil — mevcut Agent input altyapısı ya da `System.Windows.Forms.Cursor` + `mouse_event` P/Invoke) ve Tesseract engine yaşam döngüsü Windows-only; birim testte doğrulanmaz. Bu task'te **saf, test edilebilir kısım OCR metin eşleştirme normalizasyonudur**; kanalın geri kalanı golden testli değildir (E2E ayrı).

- [ ] **Step 1: Failing test yaz (OCR metin normalizasyon eşleşmesi)**

`tests/RPA.Agent.Tests/Vision/OcrTextMatchTests.cs`:
```csharp
namespace RPA.Agent.Tests.Vision;

using RPA.Agent.Vision;
using Xunit;

public class OcrTextMatchTests
{
    [Theory]
    [InlineData("  Kaydet  ", "kaydet", "contains", true)]
    [InlineData("Kaydet ve Kapat", "kaydet", "contains", true)]
    [InlineData("Kaydet ve Kapat", "kaydet", "exact", false)]
    [InlineData("Kaydet", "kaydet", "exact", true)]
    [InlineData("İptal", "iptal", "contains", true)]  // TR büyük İ / küçük i toleransı
    [InlineData("Save", "kaydet", "contains", false)]
    public void Matches_NormalizesWhitespaceAndCase(string ocrWord, string query, string mode, bool expected)
    {
        Assert.Equal(expected, OcrTextMatch.Matches(ocrWord, query, mode));
    }
}
```

- [ ] **Step 2: Testi çalıştır — FAIL**

Run: `dotnet test tests/RPA.Agent.Tests --filter FullyQualifiedName~OcrTextMatchTests`
Expected: FAIL — `OcrTextMatch` yok.

- [ ] **Step 3: OcrTextMatch yardımcısını yaz**

`src/RPA.Agent/Vision/OcrTextMatch.cs`:
```csharp
namespace RPA.Agent.Vision;

using System.Globalization;

/// <summary>OCR kelime kutuları ile aranan metni normalize ederek (boşluk/case, TR-duyarlı) eşleştirir.</summary>
public static class OcrTextMatch
{
    public static bool Matches(string? ocrWord, string query, string matchMode)
    {
        var a = Normalize(ocrWord);
        var b = Normalize(query);
        if (b.Length == 0)
        {
            return false;
        }
        return string.Equals(matchMode, "exact", StringComparison.Ordinal)
            ? string.Equals(a, b, StringComparison.Ordinal)
            : a.Contains(b, StringComparison.Ordinal);
    }

    private static string Normalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return string.Empty;
        }
        var collapsed = string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.ToLower(new CultureInfo("tr-TR")).Trim();
    }
}
```

- [ ] **Step 4: Testi çalıştır — PASS**

Run: `dotnet test tests/RPA.Agent.Tests --filter FullyQualifiedName~OcrTextMatchTests`
Expected: PASS (6 test).

- [ ] **Step 5: ScreenCapture (GDI) yaz**

`src/RPA.Agent/Vision/ScreenCapture.cs`:
```csharp
namespace RPA.Agent.Vision;

using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using OpenCvSharp;
using OpenCvSharp.Extensions;

/// <summary>GDI ile tam ekran veya bölge yakalar ve OpenCv Mat'e dönüştürür (BGR).</summary>
[SupportedOSPlatform("windows")]
public static class ScreenCapture
{
    public static Mat Capture(int? x, int? y, int? width, int? height)
    {
        var bounds = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        var rx = x ?? bounds.X;
        var ry = y ?? bounds.Y;
        var rw = width ?? bounds.Width;
        var rh = height ?? bounds.Height;

        using var bmp = new Bitmap(rw, rh, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(rx, ry, 0, 0, new Size(rw, rh));
        }
        return BitmapConverter.ToMat(bmp);
    }

    public static Mat DecodeBase64Png(string base64)
    {
        var bytes = Convert.FromBase64String(base64);
        return Cv2.ImDecode(bytes, ImreadModes.Color);
    }
}
```
> `System.Windows.Forms` referansı için csproj'de `<UseWindowsForms>true</UseWindowsForms>` gerekli olabilir — mevcut Agent WinForms/GDI kullanımını (Desktop picker) referans al; zaten varsa dokunma.

- [ ] **Step 6: TesseractOpenCvVisionChannel'i yaz**

`src/RPA.Agent/Vision/TesseractOpenCvVisionChannel.cs`:
```csharp
namespace RPA.Agent.Vision;

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using RPA.Domain.Interfaces;
using RPA.Domain.ValueObjects;
using Tesseract;
using SystemException = RPA.Domain.Exceptions.SystemException;

/// <summary>
/// IVisionAutomationChannel'ın OpenCvSharp (template) + Tesseract (OCR) implementasyonu.
/// Etkileşimli masaüstü oturumu gerektirir. Bulunamadı/timeout → SystemException.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TesseractOpenCvVisionChannel : IVisionAutomationChannel
{
    private readonly ILogger<TesseractOpenCvVisionChannel> _logger;
    private readonly string _tessdataPath;

    public TesseractOpenCvVisionChannel(ILogger<TesseractOpenCvVisionChannel> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tessdataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");
    }

    public async Task ClickImageAsync(string imageBase64, double confidence, string? clickType, int timeoutMs)
    {
        var match = await PollForImageAsync(imageBase64, confidence, timeoutMs);
        if (match is null)
        {
            throw new SystemException("Görüntü ekranda bulunamadı (timeout).");
        }
        DoClick(match.CenterX, match.CenterY, clickType);
    }

    public async Task WaitForImageAsync(string imageBase64, double confidence, int timeoutMs)
    {
        var match = await PollForImageAsync(imageBase64, confidence, Math.Max(timeoutMs, 1));
        if (match is null)
        {
            throw new SystemException("Görüntü beklenirken zaman aşımı.");
        }
    }

    public async Task<bool> ImageExistsAsync(string imageBase64, double confidence, int timeoutMs)
        => await PollForImageAsync(imageBase64, confidence, timeoutMs) is not null;

    public Task<string> GetTextAsync(int? x, int? y, int? width, int? height, string language)
    {
        using var region = ScreenCapture.Capture(x, y, width, height);
        var (text, _) = RunOcr(region, language);
        return Task.FromResult(text);
    }

    public async Task ClickTextAsync(string text, string language, string matchMode, string? clickType, int timeoutMs)
    {
        var box = await PollForTextAsync(text, language, matchMode, timeoutMs);
        if (box is null)
        {
            throw new SystemException($"Metin ekranda bulunamadı: '{text}' (timeout).");
        }
        DoClick(box.CenterX, box.CenterY, clickType);
    }

    public async Task<bool> TextExistsAsync(string text, string language, string matchMode, int timeoutMs)
        => await PollForTextAsync(text, language, matchMode, timeoutMs) is not null;

    private async Task<VisionMatch?> PollForImageAsync(string imageBase64, double confidence, int timeoutMs)
    {
        using var needle = ScreenCapture.DecodeBase64Png(imageBase64);
        var sw = Stopwatch.StartNew();
        do
        {
            using var screen = ScreenCapture.Capture(null, null, null, null);
            var match = TemplateMatcher.FindBest(screen, needle, confidence);
            if (match is not null)
            {
                return match;
            }
            if (sw.ElapsedMilliseconds >= timeoutMs)
            {
                break;
            }
            await Task.Delay(250);
        }
        while (sw.ElapsedMilliseconds < timeoutMs);
        return null;
    }

    private async Task<VisionMatch?> PollForTextAsync(string text, string language, string matchMode, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        do
        {
            using var screen = ScreenCapture.Capture(null, null, null, null);
            var (_, words) = RunOcr(screen, language);
            var hit = words.FirstOrDefault(w => OcrTextMatch.Matches(w.Text, text, matchMode));
            if (hit is not null)
            {
                return hit.Box;
            }
            if (sw.ElapsedMilliseconds >= timeoutMs)
            {
                break;
            }
            await Task.Delay(250);
        }
        while (sw.ElapsedMilliseconds < timeoutMs);
        return null;
    }

    private (string Text, List<OcrWord> Words) RunOcr(Mat image, string language)
    {
        try
        {
            using var engine = new TesseractEngine(_tessdataPath, language, EngineMode.Default);
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
        catch (Exception ex) when (ex is not RPA.Domain.Exceptions.SystemException)
        {
            throw new SystemException($"OCR başarısız: {ex.Message}", ex);
        }
    }

    private void DoClick(int x, int y, string? clickType)
    {
        System.Windows.Forms.Cursor.Position = new System.Drawing.Point(x, y);
        var kind = string.IsNullOrWhiteSpace(clickType) ? "left" : clickType.ToLowerInvariant();
        MouseDownUp(kind == "right" ? RightDown : LeftDown, kind == "right" ? RightUp : LeftUp);
        if (kind == "double")
        {
            MouseDownUp(LeftDown, LeftUp);
        }
        _logger.LogInformation("Vision tıklama: ({X},{Y}) {Kind}", x, y, kind);
    }

    private static void MouseDownUp(uint down, uint up)
    {
        mouse_event(down, 0, 0, 0, UIntPtr.Zero);
        mouse_event(up, 0, 0, 0, UIntPtr.Zero);
    }

    private const uint LeftDown = 0x0002, LeftUp = 0x0004, RightDown = 0x0008, RightUp = 0x0010;

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    private sealed record OcrWord(string Text, VisionMatch Box);
}
```
> **Not:** `Tesseract` 5.x API adları (`TesseractEngine`, `Pix.LoadFromMemory`, `page.GetIterator`, `TryGetBoundingBox`) sürüme göre küçük farklılık gösterebilir; derleme hatası olursa yüklü Tesseract sürümünün API'sine uydur (davranış aynı kalmalı). Mevcut `RPA.Domain.Exceptions.SystemException` sınıfının ctor imzasını doğrula.

- [ ] **Step 7: tessdata dosyalarını output'a kopyalat**

Test/çalışma için `src/RPA.Agent/tessdata/` klasörüne `tur.traineddata`, `eng.traineddata`, `deu.traineddata` indir (https://github.com/tesseract-ocr/tessdata_fast). `RPA.Agent.csproj`:
```xml
  <ItemGroup>
    <None Include="tessdata\**\*.traineddata" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```
> Dosyalar büyük (~15-40MB/dil). `.gitignore`'a eklenip dağıtımda ayrı sağlanabilir; ekip kararına göre. Plan bunları repoya commit etmez — indirme adımı README/setup'a not düşülür.

- [ ] **Step 8: Derle + OCR testleri çalıştır — PASS**

Run: `dotnet build src/RPA.Agent/RPA.Agent.csproj`
Expected: derleme başarılı.
Run: `dotnet test tests/RPA.Agent.Tests --filter FullyQualifiedName~OcrTextMatchTests`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/RPA.Agent/Vision/ src/RPA.Agent/RPA.Agent.csproj tests/RPA.Agent.Tests/Vision/OcrTextMatchTests.cs
git commit -m "feat(agent): TesseractOpenCvVisionChannel (Paket F)

GDI ekran yakalama + çok-ölçekli template matching + Tesseract çok-dilli OCR +
gerçek fare tıklama. Bulunamadı/timeout → SystemException.

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 6: Agent — 🎯 image region picker + SpyElementMessage.FromImage + coordinator kind:"image"

**Files:**
- Modify: `src/RPA.Infrastructure/UISpy/SapGuiElementSender.cs` (SpyElementMessage: `ImageBase64`, `Region` alanları + `FromImage`)
- Modify: `src/RPA.Agent/UISpy/SpySessionCoordinator.cs` (`IImageRegionPicker` arayüzü + ctor param + `kind:"image"` dalı)
- Create: `src/RPA.Agent/UISpy/GdiImageRegionPicker.cs`
- Test: `tests/RPA.Infrastructure.Tests/UISpy/SpyElementMessageTests.cs` + `tests/RPA.Agent.Tests/UISpy/SpySessionCoordinatorImageTests.cs`

**Interfaces:**
- Consumes: `SpySessionCoordinator` mevcut yapısı; `ISpyElementTransport`.
- Produces: `RPA.Agent.UISpy.IImageRegionPicker { Task<ImagePick?> DetectOnceAsync(CancellationToken) }`; `RPA.Agent.UISpy.ImagePick(string? ImageBase64, string? RegionJson)`; `SpyElementMessage.FromImage(ImagePick pick, Guid sessionId)` (`Kind="image"`, `ImageBase64`, `Region` doldurur, `ElementId` = "image").

- [ ] **Step 1: Failing test yaz (FromImage)**

`tests/RPA.Infrastructure.Tests/UISpy/SpyElementMessageTests.cs`:
```csharp
namespace RPA.Infrastructure.Tests.UISpy;

using RPA.Infrastructure.UISpy;
using Xunit;

public class SpyElementMessageTests
{
    [Fact]
    public void FromImage_SetsKindAndPayload()
    {
        var sessionId = Guid.NewGuid();
        var msg = SpyElementMessage.FromImage("BASE64PNG", "{\"x\":10,\"y\":20,\"width\":30,\"height\":40}", sessionId);

        Assert.Equal("image", msg.Kind);
        Assert.Equal(sessionId, msg.SessionId);
        Assert.Equal("BASE64PNG", msg.ImageBase64);
        Assert.Equal("{\"x\":10,\"y\":20,\"width\":30,\"height\":40}", msg.Region);
    }
}
```

- [ ] **Step 2: Testi çalıştır — FAIL**

Run: `dotnet test tests/RPA.Infrastructure.Tests --filter FullyQualifiedName~SpyElementMessageTests`
Expected: FAIL — `ImageBase64`/`Region`/`FromImage` yok.

- [ ] **Step 3: SpyElementMessage'a image alanları + FromImage ekle**

`SapGuiElementSender.cs` — `SpyElementMessage` record'una (diğer nullable alanların yanına):
```csharp
    public string? ImageBase64 { get; init; }
    public string? Region { get; init; }
```
`FromDesktop`'un altına:
```csharp
    /// <summary>🎯 image picker'dan gelen bölge/görüntü seçiminden mesaj oluşturur.</summary>
    public static SpyElementMessage FromImage(string? imageBase64, string? regionJson, Guid sessionId)
    {
        return new SpyElementMessage
        {
            SessionId = sessionId,
            Kind = "image",
            ElementId = "image",
            ImageBase64 = imageBase64,
            Region = regionJson,
            Selector = imageBase64 ?? regionJson,
            Enabled = true,
            Changeable = true,
        };
    }
```

- [ ] **Step 4: Testi çalıştır — PASS**

Run: `dotnet test tests/RPA.Infrastructure.Tests --filter FullyQualifiedName~SpyElementMessageTests`
Expected: PASS.

- [ ] **Step 5: Coordinator'a IImageRegionPicker + kind:"image" dalı — failing test**

`tests/RPA.Agent.Tests/UISpy/SpySessionCoordinatorImageTests.cs` (mevcut `SpySessionCoordinatorTests.cs` desenini taban al):
```csharp
namespace RPA.Agent.Tests.UISpy;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RPA.Agent.UISpy;
using RPA.Infrastructure.UISpy;
using Xunit;

public class SpySessionCoordinatorImageTests
{
    [Fact]
    public async Task Start_ImageKind_SendsImageMessage()
    {
        var sessionId = Guid.NewGuid();
        var transport = new Mock<ISpyElementTransport>();
        var imagePicker = new Mock<IImageRegionPicker>();
        imagePicker.Setup(p => p.DetectOnceAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new ImagePick("BASE64", null));
        var options = Options.Create(new SpySessionOptions { TimeoutSeconds = 5 });

        var coordinator = new SpySessionCoordinator(
            Mock.Of<ISapGuiSinglePicker>(), transport.Object, options,
            NullLogger<SpySessionCoordinator>.Instance,
            imagePicker: imagePicker.Object);

        await coordinator.StartAsync(sessionId, "image");

        transport.Verify(t => t.SendAsync(
            It.Is<SpyElementMessage>(m => m.Kind == "image" && m.ImageBase64 == "BASE64"),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
```
> Ctor imzası genişleyeceği için mevcut `SpySessionCoordinatorTests.cs`'teki çağrılar da yeni opsiyonel parametreyle uyumlu kalmalı (opsiyonel `= null` → mevcut testler değişmez).

- [ ] **Step 6: Testi çalıştır — FAIL**

Run: `dotnet test tests/RPA.Agent.Tests --filter FullyQualifiedName~SpySessionCoordinatorImageTests`
Expected: FAIL — `IImageRegionPicker`/`ImagePick`/image dalı yok.

- [ ] **Step 7: SpySessionCoordinator'ı genişlet**

`SpySessionCoordinator.cs` — arayüz + kayıt tanımları bölümüne:
```csharp
/// <summary>🎯 image bölge picker'ı — ekranda dikdörtgen çiz, PNG/koordinat döndür.</summary>
public interface IImageRegionPicker
{
    Task<ImagePick?> DetectOnceAsync(CancellationToken cancellationToken = default);
}

/// <summary>Image picker sonucu: base64 PNG (image alanı için) ve/veya {x,y,width,height} JSON (region alanı için).</summary>
public sealed record ImagePick(string? ImageBase64, string? RegionJson);
```
Ctor'a opsiyonel parametre ekle:
```csharp
    private readonly IImageRegionPicker? _imagePicker;
```
ctor imza sonuna `, IImageRegionPicker? imagePicker = null` ekle ve gövdede `_imagePicker = imagePicker;` ata.
`StartAsync` içinde `isWeb` yanına:
```csharp
        var isImage = string.Equals(kind, "image", StringComparison.OrdinalIgnoreCase);
```
`if (!isSap && !isDesktop && !isWeb)` koşulunu `&& !isImage` ile genişlet.
`if (isWeb && _webPicker is null)` bloğunun altına:
```csharp
        if (isImage && _imagePicker is null)
        {
            throw new InvalidOperationException("Image picker bu ortamda kayıtlı değil (yalnız Windows).");
        }
```
Mesaj üretim zincirine (`else if (isWeb)` bloğunun altına, `else` SAP'tan önce):
```csharp
            else if (isImage)
            {
                var pick = await _imagePicker!.DetectOnceAsync(linkedCts.Token);
                message = pick is null ? null : SpyElementMessage.FromImage(pick.ImageBase64, pick.RegionJson, sessionId);
            }
```

- [ ] **Step 8: Testi çalıştır — PASS + mevcut coordinator testleri**

Run: `dotnet test tests/RPA.Agent.Tests --filter FullyQualifiedName~SpySessionCoordinator`
Expected: PASS (yeni + mevcut testler).

- [ ] **Step 9: GdiImageRegionPicker'ı yaz**

`src/RPA.Agent/UISpy/GdiImageRegionPicker.cs` — tam ekran yarı saydam overlay WinForms formu; fareyle dikdörtgen çiz, `Esc` iptal; seçilen bölgeyi `ScreenCapture.Capture` ile yakala, PNG'yi base64'e çevir, `ImagePick(base64, regionJson)` döndür. `FlaUiDesktopSinglePicker`'ın overlay/ShowWindow desenini referans al.
```csharp
namespace RPA.Agent.UISpy;

using System.Runtime.Versioning;
using RPA.Agent.Vision;

/// <summary>
/// GDI overlay ile ekranda dikdörtgen bölge seçtiren image picker. Seçilen bölgenin PNG'sini
/// base64 olarak ve {x,y,width,height} JSON'unu döndürür. Esc → iptal (null).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GdiImageRegionPicker : IImageRegionPicker
{
    public Task<ImagePick?> DetectOnceAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rect = RegionOverlayForm.SelectRegion(cancellationToken); // null → iptal
        if (rect is null)
        {
            return Task.FromResult<ImagePick?>(null);
        }
        var (x, y, w, h) = rect.Value;
        using var mat = ScreenCapture.Capture(x, y, w, h);
        var png = mat.ImEncode(".png");
        var base64 = Convert.ToBase64String(png);
        var regionJson = $"{{\"x\":{x},\"y\":{y},\"width\":{w},\"height\":{h}}}";
        return Task.FromResult<ImagePick?>(new ImagePick(base64, regionJson));
    }
}
```
> `RegionOverlayForm.SelectRegion` — tam ekranı kaplayan, `Opacity ~0.3`, mouse-drag ile dikdörtgen çizen, `Esc`'te null döndüren WinForms formu. Mevcut Agent WinForms picker (FlaUiDesktopSinglePicker) tek-ekran/overlay desenini birebir taklit et; STA thread gereksinimini oradan al.

- [ ] **Step 10: Derle — PASS**

Run: `dotnet build src/RPA.Agent/RPA.Agent.csproj`
Expected: derleme başarılı.

- [ ] **Step 11: Commit**

```bash
git add src/RPA.Infrastructure/UISpy/SapGuiElementSender.cs src/RPA.Agent/UISpy/SpySessionCoordinator.cs src/RPA.Agent/UISpy/GdiImageRegionPicker.cs tests/RPA.Infrastructure.Tests/UISpy/SpyElementMessageTests.cs tests/RPA.Agent.Tests/UISpy/SpySessionCoordinatorImageTests.cs
git commit -m "feat(agent): image region picker + kind:image (Paket F)

SpyElementMessage.FromImage + IImageRegionPicker/GdiImageRegionPicker +
SpySessionCoordinator image dalı. 🎯 bölge seç → base64 PNG göm.

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 7: Agent DI wiring + kontrat notu (CLAUDE.md)

**Files:**
- Modify: `src/RPA.Agent/AgentServiceCollectionExtensions.cs` (Windows koşulunda `IVisionAutomationChannel` = `TesseractOpenCvVisionChannel`, `IImageRegionPicker` = `GdiImageRegionPicker`)
- Modify: `CLAUDE.md` (Kontrat Değişikliği — Paket F notu)

**Interfaces:**
- Consumes: Task 5 `TesseractOpenCvVisionChannel`, Task 6 `GdiImageRegionPicker`; mevcut `AddAgentCore` Windows-koşullu kayıt bloğu (line ~105-109, `IDesktopSinglePicker`/`IWebSinglePicker` kayıtlarının olduğu yer).

- [ ] **Step 1: Agent DI kaydını ekle**

`AgentServiceCollectionExtensions.cs` — mevcut Windows koşullu blokta (`IDesktopSinglePicker`, `IWebSinglePicker` kayıtlarının olduğu `if (OperatingSystem.IsWindows())` içi):
```csharp
            services.AddSingleton<RPA.Domain.Interfaces.IVisionAutomationChannel, Vision.TesseractOpenCvVisionChannel>();
            services.AddSingleton<IImageRegionPicker, GdiImageRegionPicker>();
```
> `IVisionAutomationChannel` Infrastructure'da `TryAddSingleton` ile Unavailable kaydedildi (Task 3); Agent burada `AddSingleton` ile gerçek implementasyonu **ekler**. Kayıt sırası Desktop kanalıyla aynı olmalı (Agent, Infrastructure'dan sonra çağrılıyorsa `AddSingleton` son kazanır — Desktop deseniyle bire bir aynı yaklaşımı uygula; farklıysa Desktop'un yaptığını kopyala).

- [ ] **Step 2: Agent'ı derle + tüm testler**

Run: `dotnet build src/RPA.Agent/RPA.Agent.csproj`
Expected: başarılı.
Run: `dotnet test`
Expected: tüm çözüm testleri PASS.

- [ ] **Step 3: CLAUDE.md kontrat notu ekle**

`CLAUDE.md`'de son "Kontrat Değişikliği" bölümünün altına:
```markdown
## Kontrat Değişikliği — 2026-07-12 (Paket F — Görüntü/OCR Fallback Otomasyonu)

Erişilebilirlik ağacı olmayan uygulamalar için piksel + metin tabanlı otomasyon kanalı.

- **Yeni arayüz:** `IVisionAutomationChannel` (`src/RPA.Domain/Interfaces/`) — template matching +
  OCR. `IDesktopAutomationChannel` kardeşi. Metotlar: ClickImageAsync, WaitForImageAsync,
  ImageExistsAsync, GetTextAsync, ClickTextAsync, TextExistsAsync. Yeni value object `VisionMatch`.
- **Yeni aktivite ailesi:** `Vision.*` (kategori "Görüntü", capability `vision`) —
  Click/WaitFor/Exists/GetText/ClickText/TextExists. Katalog `ActivityRegistry.RegisterVision`;
  keyed DI `WorkflowServiceCollectionExtensions`. OCR çok dilli (`tur+eng+deu`).
- **İmplementasyon:** `TesseractOpenCvVisionChannel` (`RPA.Agent/Vision/`) — OpenCvSharp4 (template)
  + Tesseract (OCR) + GDI ekran yakalama + gerçek fare. Windows-only, `AddAgentCore`'da kayıtlı.
  Non-agent süreçlerde `UnavailableVisionAutomationChannel` (TryAddSingleton).
- **🎯 image picker:** `SpyElementMessage`'a `Kind="image"`, `ImageBase64`, `Region` + `FromImage`.
  `ActivityParameter.PickerKind` yeni değer `"image"`. `StudioHub.StartSpy` `kind:"image"` kabul eder.
  Yeni arayüz `IImageRegionPicker` / `GdiImageRegionPicker` (bölge seç → base64 PNG göm).
  `SpySessionCoordinator` opsiyonel `IImageRegionPicker? imagePicker` parametresi aldı.
- **Anchor Faz 2'ye ertelendi:** `TemplateMatcher.FindAll` çok-eşleşme döndürecek şekilde hazır.

Etkilenen paketler: Studio picker metadata tüketicileri (yeni `image` kind), Agent UI Spy transport.
SAP/Web/Desktop picker'lar etkilenmez (additive).
Gerekçe: UIA/DOM sunmayan uygulamalar için (eski Win32, custom-render) otomasyon boşluğu.
```

- [ ] **Step 4: Commit**

```bash
git add src/RPA.Agent/AgentServiceCollectionExtensions.cs CLAUDE.md
git commit -m "feat(agent): Vision kanalı DI wiring + kontrat notu (Paket F)

TesseractOpenCvVisionChannel + GdiImageRegionPicker Windows kaydı.
CLAUDE.md Kontrat Değişikliği — Paket F.

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

- [ ] **Step 5: Studio picker `image` kind desteği (varsa)**

Studio tarafında `SpyKind`/`spy.service.ts` `image` değerini kabul ediyor mu doğrula. `PickerKind="image"` parametrelerde 🎯 düğmesi `spy.pick("image")` çağırıyorsa ve base64 sonucu parametreye yazıp thumbnail gösteriyorsa değişiklik gerekmez. Eksikse: `SpyKind` union'ına `'image'` ekle, `ReceiveDetectedElement` handler'ında `Kind==='image'` için `ImageBase64`'ü parametre değerine yaz + küçük `<img>` önizleme göster.
> Bu adım Studio kod tabanının durumuna bağlı; mevcut web/desktop picker Studio akışını referans al. Değişiklik gerekirse ayrı commit:
```bash
git commit -m "feat(studio): Vision image picker önizleme (Paket F)"
```

---

## Notlar / Kalan İşler (Faz 2+)

- **Anchor** (komşuya göre bulma): `TemplateMatcher.FindAll` + OCR çok-kelime konumları + yön/mesafe skorlama + iki-adımlı picker + `anchorImage`/`anchorText`/`direction`/`maxDistance` parametreleri.
- **tessdata dağıtımı:** `.traineddata` dosyaları büyük; kurulum/setup dokümanına indirme adımı; robot imajına dahil edilmesi.
- **E2E görsel testler:** gerçek ekran gerektirir; Windows'lu ayrı CI job'unda (Desktop.* deseniyle aynı) çalıştırılır.

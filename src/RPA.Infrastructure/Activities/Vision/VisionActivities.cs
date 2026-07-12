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

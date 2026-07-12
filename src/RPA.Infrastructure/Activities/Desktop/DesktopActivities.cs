namespace RPA.Infrastructure.Activities.Desktop;

using RPA.Domain.Interfaces;
using BusinessException = RPA.Domain.Exceptions.BusinessException;

// Windows Masaüstü Otomasyonu — Desktop.* aktivite ailesi (Spec Bölüm 5, Paket E).
// Her aktivite IDesktopAutomationChannel üzerinden çalışır (FlaUI/UIA implementasyonu
// RPA.Agent'ta yaşar). Aktiviteler platform-nötrdür; yalnız arayüzü çağırır, böylece
// birim testlerde kanal mock'lanabilir.
//
// Selector formatı UIA yoludur (AutomationId öncelikli). Input doğrulama hataları
// BusinessException (input hatası → Business); element bulunamadı/timeout kanalda
// SystemException olarak fırlatılır.

internal static class DesktopMeta
{
    public const string Category = "Masaüstü";
    public const string Capability = "desktop";

    public static ActivityParameter Selector(string desc = "UIA selector yolu (AutomationId öncelikli).") =>
        new() { Name = "selector", Type = "string", Required = true, Description = desc, PickerKind = "desktop" };
}

/// <summary>Çalışan bir uygulamanın penceresine bağlanır.</summary>
public sealed class DesktopAttachActivity : IActivity
{
    private readonly IDesktopAutomationChannel _channel;
    public DesktopAttachActivity(IDesktopAutomationChannel channel)
        => _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public ActivityMetadata GetMetadata() => new()
    {
        ActivityId = "Desktop.Attach",
        DisplayName = "Uygulamaya Bağlan",
        Category = DesktopMeta.Category,
        Description = "Çalışan bir Windows uygulamasının penceresine bağlanır.",
        Inputs = new()
        {
            new ActivityParameter { Name = "processName", Type = "string", Required = false, Description = "Süreç adı (örn. notepad)." },
            new ActivityParameter { Name = "windowTitle", Type = "string", Required = false, Description = "Pencere başlığı (regex desteklenir)." },
        },
        Outputs = new() { new ActivityParameter { Name = "windowHandle", Type = "string", Required = false, Description = "Pencere tanıtıcısı." } },
        RequiredCapabilities = new() { DesktopMeta.Capability },
    };

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var processName = context.GetVariable<string?>("processName");
        var windowTitle = context.GetVariable<string?>("windowTitle");
        if (string.IsNullOrWhiteSpace(processName) && string.IsNullOrWhiteSpace(windowTitle))
        {
            throw new BusinessException("'processName' veya 'windowTitle' parametrelerinden biri zorunludur.");
        }

        context.Log($"Masaüstü uygulamasına bağlanılıyor: {processName ?? windowTitle}");
        var handle = await _channel.AttachAsync(NullIfBlank(processName), NullIfBlank(windowTitle));
        context.SetVariable("windowHandle", handle);
        return new() { ["windowHandle"] = handle };
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}

/// <summary>Bir uygulamayı başlatır ve ana penceresine bağlanır.</summary>
public sealed class DesktopLaunchActivity : IActivity
{
    private readonly IDesktopAutomationChannel _channel;
    public DesktopLaunchActivity(IDesktopAutomationChannel channel)
        => _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public ActivityMetadata GetMetadata() => new()
    {
        ActivityId = "Desktop.Launch",
        DisplayName = "Uygulama Başlat",
        Category = DesktopMeta.Category,
        Description = "Bir Windows uygulamasını başlatır ve penceresine bağlanır.",
        Inputs = new()
        {
            new ActivityParameter { Name = "path", Type = "string", Required = true, Description = "Çalıştırılabilir yol." },
            new ActivityParameter { Name = "arguments", Type = "string", Required = false, Description = "Komut satırı argümanları." },
            new ActivityParameter { Name = "waitForIdle", Type = "bool", Required = false, Description = "Pencere hazır olana kadar bekle.", DefaultValue = true },
        },
        Outputs = new() { new ActivityParameter { Name = "windowHandle", Type = "string", Required = false, Description = "Pencere tanıtıcısı." } },
        RequiredCapabilities = new() { DesktopMeta.Capability },
    };

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var path = context.GetVariable<string?>("path");
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new BusinessException("'path' parametresi boş olamaz.");
        }

        var arguments = context.GetVariable<string?>("arguments");
        var waitForIdle = context.GetVariable<bool>("waitForIdle");

        context.Log($"Masaüstü uygulaması başlatılıyor: {path}");
        var handle = await _channel.LaunchAsync(path, string.IsNullOrWhiteSpace(arguments) ? null : arguments, waitForIdle);
        context.SetVariable("windowHandle", handle);
        return new() { ["windowHandle"] = handle };
    }
}

/// <summary>Bir elemente tıklar (buton, menü).</summary>
public sealed class DesktopClickActivity : IActivity
{
    private readonly IDesktopAutomationChannel _channel;
    public DesktopClickActivity(IDesktopAutomationChannel channel)
        => _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public ActivityMetadata GetMetadata() => new()
    {
        ActivityId = "Desktop.Click",
        DisplayName = "Masaüstü Tıkla",
        Category = DesktopMeta.Category,
        Description = "Masaüstü uygulamasındaki bir elemente tıklar.",
        Inputs = new()
        {
            DesktopMeta.Selector(),
            new ActivityParameter { Name = "clickType", Type = "string", Required = false, Description = "Tıklama türü.", Options = new() { "left", "right", "double" }, DefaultValue = "left" },
        },
        Outputs = new(),
        RequiredCapabilities = new() { DesktopMeta.Capability },
    };

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var selector = RequireSelector(context);
        var clickType = context.GetVariable<string?>("clickType");
        context.Log($"Masaüstü tıklanıyor: {selector} ({clickType ?? "left"})");
        await _channel.ClickAsync(selector, string.IsNullOrWhiteSpace(clickType) ? null : clickType);
        return new();
    }

    internal static string RequireSelector(IActivityExecutionContext context)
    {
        var selector = context.GetVariable<string?>("selector");
        if (string.IsNullOrWhiteSpace(selector))
        {
            throw new BusinessException("'selector' parametresi boş olamaz.");
        }
        return selector;
    }
}

/// <summary>Bir textbox'ı metinle doldurur.</summary>
public sealed class DesktopSetTextActivity : IActivity
{
    private readonly IDesktopAutomationChannel _channel;
    public DesktopSetTextActivity(IDesktopAutomationChannel channel)
        => _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public ActivityMetadata GetMetadata() => new()
    {
        ActivityId = "Desktop.SetText",
        DisplayName = "Masaüstü Metin Yaz",
        Category = DesktopMeta.Category,
        Description = "Bir alana (textbox) metin yazar.",
        Inputs = new()
        {
            DesktopMeta.Selector(),
            new ActivityParameter { Name = "text", Type = "string", Required = false, Description = "Yazılacak metin." },
        },
        Outputs = new(),
        RequiredCapabilities = new() { DesktopMeta.Capability },
    };

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var selector = DesktopClickActivity.RequireSelector(context);
        var text = context.GetVariable<string?>("text") ?? string.Empty;
        context.Log($"Masaüstü metin yazılıyor: {selector}");
        await _channel.SetTextAsync(selector, text);
        return new();
    }
}

/// <summary>Bir elementin metnini okur.</summary>
public sealed class DesktopGetTextActivity : IActivity
{
    private readonly IDesktopAutomationChannel _channel;
    public DesktopGetTextActivity(IDesktopAutomationChannel channel)
        => _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public ActivityMetadata GetMetadata() => new()
    {
        ActivityId = "Desktop.GetText",
        DisplayName = "Masaüstü Metin Oku",
        Category = DesktopMeta.Category,
        Description = "Bir elementin metnini okur; sonuç 'text' değişkenine yazılır.",
        Inputs = new() { DesktopMeta.Selector() },
        Outputs = new() { new ActivityParameter { Name = "text", Type = "string", Required = false, Description = "Okunan metin." } },
        RequiredCapabilities = new() { DesktopMeta.Capability },
    };

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var selector = DesktopClickActivity.RequireSelector(context);
        context.Log($"Masaüstü metin okunuyor: {selector}");
        var text = await _channel.GetTextAsync(selector);
        context.SetVariable("text", text);
        return new() { ["text"] = text };
    }
}

/// <summary>Combobox/liste/menüde bir öğe seçer.</summary>
public sealed class DesktopSelectItemActivity : IActivity
{
    private readonly IDesktopAutomationChannel _channel;
    public DesktopSelectItemActivity(IDesktopAutomationChannel channel)
        => _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public ActivityMetadata GetMetadata() => new()
    {
        ActivityId = "Desktop.SelectItem",
        DisplayName = "Masaüstü Öğe Seç",
        Category = DesktopMeta.Category,
        Description = "Combobox / liste / menüde bir öğe seçer.",
        Inputs = new()
        {
            DesktopMeta.Selector(),
            new ActivityParameter { Name = "item", Type = "string", Required = true, Description = "Seçilecek öğe metni." },
        },
        Outputs = new(),
        RequiredCapabilities = new() { DesktopMeta.Capability },
    };

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var selector = DesktopClickActivity.RequireSelector(context);
        var item = context.GetVariable<string?>("item");
        if (string.IsNullOrWhiteSpace(item))
        {
            throw new BusinessException("'item' parametresi boş olamaz.");
        }
        context.Log($"Masaüstü öğe seçiliyor: {selector} → {item}");
        await _channel.SelectItemAsync(selector, item);
        return new();
    }
}

/// <summary>Klavye tuşları gönderir (tarih yazma, kısayol).</summary>
public sealed class DesktopSendKeysActivity : IActivity
{
    private readonly IDesktopAutomationChannel _channel;
    public DesktopSendKeysActivity(IDesktopAutomationChannel channel)
        => _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public ActivityMetadata GetMetadata() => new()
    {
        ActivityId = "Desktop.SendKeys",
        DisplayName = "Masaüstü Tuş Gönder",
        Category = DesktopMeta.Category,
        Description = "Klavye tuşları gönderir. Selector boşsa aktif odağa gönderilir.",
        Inputs = new()
        {
            new ActivityParameter { Name = "selector", Type = "string", Required = false, Description = "Hedef element (opsiyonel).", PickerKind = "desktop" },
            new ActivityParameter { Name = "keys", Type = "string", Required = true, Description = "Gönderilecek tuşlar (örn. '^s', '09.07.2026')." },
        },
        Outputs = new(),
        RequiredCapabilities = new() { DesktopMeta.Capability },
    };

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var keys = context.GetVariable<string?>("keys");
        if (string.IsNullOrEmpty(keys))
        {
            throw new BusinessException("'keys' parametresi boş olamaz.");
        }
        var selector = context.GetVariable<string?>("selector");
        context.Log($"Masaüstü tuş gönderiliyor: {(string.IsNullOrWhiteSpace(selector) ? "(odak)" : selector)}");
        await _channel.SendKeysAsync(string.IsNullOrWhiteSpace(selector) ? null : selector, keys);
        return new();
    }
}

/// <summary>Bir element görünür/etkin olana kadar bekler.</summary>
public sealed class DesktopWaitForActivity : IActivity
{
    private readonly IDesktopAutomationChannel _channel;
    public DesktopWaitForActivity(IDesktopAutomationChannel channel)
        => _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public ActivityMetadata GetMetadata() => new()
    {
        ActivityId = "Desktop.WaitFor",
        DisplayName = "Masaüstü Bekle",
        Category = DesktopMeta.Category,
        Description = "Bir element görünür/etkin olana kadar bekler. Süre aşımı → System hatası.",
        Inputs = new()
        {
            DesktopMeta.Selector(),
            new ActivityParameter { Name = "timeoutMs", Type = "int", Required = false, Description = "Zaman aşımı (ms).", DefaultValue = 10000 },
        },
        Outputs = new(),
        RequiredCapabilities = new() { DesktopMeta.Capability },
        ExceptionClassification = new ExceptionClassificationRule { Condition = "Timeout", Classification = RPA.Domain.Enums.ExceptionType.System },
    };

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var selector = DesktopClickActivity.RequireSelector(context);
        var timeoutMs = context.GetVariable<int>("timeoutMs");
        context.Log($"Masaüstü bekleniyor: {selector} ({timeoutMs} ms)");
        await _channel.WaitForAsync(selector, timeoutMs);
        return new();
    }
}

/// <summary>Ekran görüntüsü alır (element veya aktif pencere).</summary>
public sealed class DesktopScreenshotActivity : IActivity
{
    private readonly IDesktopAutomationChannel _channel;
    public DesktopScreenshotActivity(IDesktopAutomationChannel channel)
        => _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public ActivityMetadata GetMetadata() => new()
    {
        ActivityId = "Desktop.Screenshot",
        DisplayName = "Masaüstü Ekran Görüntüsü",
        Category = DesktopMeta.Category,
        Description = "Ekran görüntüsü alır; selector doluysa yalnız o element.",
        Inputs = new()
        {
            new ActivityParameter { Name = "selector", Type = "string", Required = false, Description = "Hedef element (opsiyonel).", PickerKind = "desktop" },
        },
        Outputs = new() { new ActivityParameter { Name = "path", Type = "string", Required = false, Description = "Kaydedilen PNG yolu." } },
        RequiredCapabilities = new() { DesktopMeta.Capability },
    };

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var selector = context.GetVariable<string?>("selector");
        context.Log("Masaüstü ekran görüntüsü alınıyor.");
        var path = await _channel.ScreenshotAsync(string.IsNullOrWhiteSpace(selector) ? null : selector);
        context.SetVariable("path", path);
        return new() { ["path"] = path };
    }
}

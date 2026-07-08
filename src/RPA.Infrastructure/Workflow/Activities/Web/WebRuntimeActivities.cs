namespace RPA.Infrastructure.Workflow.Activities.Web;

using System.Text.Json;
using Newtonsoft.Json.Linq;
using RPA.Domain.Interfaces;
using BusinessException = RPA.Domain.Exceptions.BusinessException;

internal static class WebActivityHelpers
{
    public static string ReadSessionId(IActivityExecutionContext context)
    {
        var sessionId = context.GetVariable<string>("session");
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            return sessionId;
        }

        throw new BusinessException("'session' parametresi bos olamaz. Once Web.Open aktivitesini calistirin.");
    }

    public static void Require(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new BusinessException($"'{name}' parametresi bos olamaz.");
        }
    }

    public static ActivityMetadata Metadata(
        string activityId,
        string displayName,
        string description,
        List<ActivityParameter> inputs,
        List<ActivityParameter>? outputs = null) => new()
    {
        ActivityId = activityId,
        DisplayName = displayName,
        Category = ActivityRegistry.CatWeb,
        Description = description,
        Inputs = inputs,
        Outputs = outputs ?? new(),
        RequiredCapabilities = new() { "web" },
    };
}

public sealed class WebGotoActivity : IActivity
{
    private readonly IWebAutomationSessionManager _sessionManager;

    public WebGotoActivity(IWebAutomationSessionManager sessionManager)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
    }

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var url = context.GetVariable<string>("url");
        WebActivityHelpers.Require(url, "url");
        var uri = NormalizeUrl(url);

        var session = WebActivityHelpers.ReadSessionId(context);
        await _sessionManager.GotoAsync(session, uri);
        context.Log($"Web adresine gidildi: {uri.AbsoluteUri}");

        return new Dictionary<string, object?>
        {
            ["session"] = session,
            ["url"] = uri.AbsoluteUri,
        };
    }

    private static Uri NormalizeUrl(string url)
    {
        var candidate = url.Trim();
        if (!candidate.Contains("://", StringComparison.Ordinal))
        {
            candidate = "https://" + candidate;
        }

        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            ? uri
            : throw new BusinessException($"Gecersiz URL: {url}");
    }

    public ActivityMetadata GetMetadata() => WebActivityHelpers.Metadata(
        "Web.Goto",
        "Adrese Git",
        "Verilen URL'ye gider.",
        new() { new ActivityParameter { Name = "url", Type = "string", Required = true } },
        new() { new ActivityParameter { Name = "session", Type = "JSON", Required = true } });
}

public sealed class WebClickActivity : IActivity
{
    private readonly IWebAutomationSessionManager _sessionManager;

    public WebClickActivity(IWebAutomationSessionManager sessionManager)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
    }

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var session = WebActivityHelpers.ReadSessionId(context);
        var steps = NormalizeSteps(context.GetVariable<object>("steps"));
        if (steps is { Count: > 0 })
        {
            for (var index = 0; index < steps.Count; index++)
            {
                var step = steps[index];
                var stepSelector = ReadStepString(step, "selector");
                WebActivityHelpers.Require(stepSelector, $"steps[{index}].selector");
                var stepSelectorValue = stepSelector!;
                var stepAction = ReadStepString(step, "action");
                if (string.IsNullOrWhiteSpace(stepAction))
                {
                    stepAction = "click";
                }

                var stepWaitSelector = ReadStepString(step, "waitSelector");
                var stepTimeoutMs = ReadStepInt(step, "timeoutMs") ?? 30000;
                await _sessionManager.ClickAsync(
                    session,
                    stepSelectorValue,
                    new WebClickOptions(
                        stepAction,
                        string.IsNullOrWhiteSpace(stepWaitSelector) ? null : stepWaitSelector,
                        stepTimeoutMs));
                context.Log($"Web tiklama adimi {index + 1} tamamlandi: {stepSelectorValue}");
            }

            return new Dictionary<string, object?>
            {
                ["session"] = session,
                ["stepCount"] = steps.Count,
            };
        }

        var selector = context.GetVariable<string>("selector");
        WebActivityHelpers.Require(selector, "selector");
        var action = context.GetVariable<string>("action");
        if (string.IsNullOrWhiteSpace(action))
        {
            action = "click";
        }

        var waitSelector = context.GetVariable<string>("waitSelector");
        var timeoutMs = context.GetVariable<int?>("timeoutMs") ?? 30000;
        var options = new WebClickOptions(action, string.IsNullOrWhiteSpace(waitSelector) ? null : waitSelector, timeoutMs);
        await _sessionManager.ClickAsync(session, selector, options);
        context.Log(options.Action.Equals("hover", StringComparison.OrdinalIgnoreCase)
            ? $"Web hover yapildi: {selector}"
            : $"Web tiklandi: {selector}");
        return new Dictionary<string, object?>
        {
            ["selector"] = selector,
            ["action"] = options.Action,
            ["waitSelector"] = options.WaitSelector,
            ["timeoutMs"] = options.TimeoutMs,
        };
    }

    public ActivityMetadata GetMetadata() => WebActivityHelpers.Metadata(
        "Web.Click",
        "Web Tikla",
        "Selector ile elemente tiklar veya hover yapar.",
        new()
        {
            new ActivityParameter { Name = "selector", Type = "string", Required = true },
            new ActivityParameter { Name = "action", Type = "string", Required = false, DefaultValue = "click" },
            new ActivityParameter { Name = "waitSelector", Type = "string", Required = false },
            new ActivityParameter { Name = "timeoutMs", Type = "int", Required = false, DefaultValue = 30000 },
            new ActivityParameter { Name = "steps", Type = "JSON", Required = false },
        });

    private static List<Dictionary<string, object?>>? NormalizeSteps(object? rawSteps)
    {
        if (rawSteps is null)
        {
            return null;
        }

        if (rawSteps is List<Dictionary<string, object?>> typedSteps)
        {
            return typedSteps;
        }

        if (rawSteps is JArray array)
        {
            return array.ToObject<List<Dictionary<string, object?>>>();
        }

        if (rawSteps is JsonElement { ValueKind: JsonValueKind.Array } element)
        {
            return element.Deserialize<List<Dictionary<string, object?>>>();
        }

        return null;
    }

    private static string? ReadStepString(IReadOnlyDictionary<string, object?> step, string key) =>
        step.TryGetValue(key, out var value) ? value?.ToString() : null;

    private static int? ReadStepInt(IReadOnlyDictionary<string, object?> step, string key)
    {
        if (!step.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            int number => number,
            long number => checked((int)number),
            double number => checked((int)number),
            decimal number => checked((int)number),
            _ => int.TryParse(value.ToString(), out var parsed) ? parsed : null,
        };
    }
}

public sealed class WebFillActivity : IActivity
{
    private readonly IWebAutomationSessionManager _sessionManager;

    public WebFillActivity(IWebAutomationSessionManager sessionManager)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
    }

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var session = WebActivityHelpers.ReadSessionId(context);
        var selector = context.GetVariable<string>("selector");
        WebActivityHelpers.Require(selector, "selector");
        var value = context.GetVariable<string>("value") ?? string.Empty;
        await _sessionManager.FillAsync(session, selector, value);
        context.Log($"Web alan dolduruldu: {selector}");
        return new Dictionary<string, object?> { ["selector"] = selector, ["value"] = value };
    }

    public ActivityMetadata GetMetadata() => WebActivityHelpers.Metadata(
        "Web.Fill",
        "Web Alan Doldur",
        "Bir input alanini doldurur.",
        new()
        {
            new ActivityParameter { Name = "selector", Type = "string", Required = true },
            new ActivityParameter { Name = "value", Type = "string", Required = true },
        });
}

public sealed class WebGetTextActivity : IActivity
{
    private readonly IWebAutomationSessionManager _sessionManager;

    public WebGetTextActivity(IWebAutomationSessionManager sessionManager)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
    }

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var session = WebActivityHelpers.ReadSessionId(context);
        var selector = context.GetVariable<string>("selector");
        WebActivityHelpers.Require(selector, "selector");
        var text = await _sessionManager.GetTextAsync(session, selector);
        context.SetVariable("text", text);
        var outputVariable = context.GetVariable<string>("outputVariable");
        if (!string.IsNullOrWhiteSpace(outputVariable))
        {
            context.SetVariable(outputVariable, text);
        }
        context.Log($"Web metin okundu: {selector}");
        var outputs = new Dictionary<string, object?> { ["text"] = text };
        if (!string.IsNullOrWhiteSpace(outputVariable))
        {
            outputs[outputVariable] = text;
        }

        return outputs;
    }

    public ActivityMetadata GetMetadata() => WebActivityHelpers.Metadata(
        "Web.GetText",
        "Web Metin Oku",
        "Elementin metnini okur.",
        new()
        {
            new ActivityParameter { Name = "selector", Type = "string", Required = true },
            new ActivityParameter { Name = "outputVariable", Type = "string", Required = false },
        },
        new() { new ActivityParameter { Name = "text", Type = "string", Required = true } });
}

public sealed class WebWaitForActivity : IActivity
{
    private readonly IWebAutomationSessionManager _sessionManager;

    public WebWaitForActivity(IWebAutomationSessionManager sessionManager)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
    }

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var session = WebActivityHelpers.ReadSessionId(context);
        var selector = context.GetVariable<string>("selector");
        WebActivityHelpers.Require(selector, "selector");
        var timeoutMs = context.GetVariable<int?>("timeoutMs") ?? 30000;
        await _sessionManager.WaitForAsync(session, selector, timeoutMs);
        context.Log($"Web beklendi: {selector} ({timeoutMs} ms)");
        return new Dictionary<string, object?> { ["selector"] = selector, ["timeoutMs"] = timeoutMs };
    }

    public ActivityMetadata GetMetadata() => WebActivityHelpers.Metadata(
        "Web.WaitFor",
        "Web Bekle",
        "Bir elementin gorunmesini/durumunu bekler.",
        new()
        {
            new ActivityParameter { Name = "selector", Type = "string", Required = true },
            new ActivityParameter { Name = "timeoutMs", Type = "int", Required = false, DefaultValue = 30000 },
        });
}

public sealed class WebDownloadActivity : IActivity
{
    public Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var selector = context.GetVariable<string>("selector");
        var targetPath = context.GetVariable<string>("targetPath");
        WebActivityHelpers.Require(selector, "selector");
        WebActivityHelpers.Require(targetPath, "targetPath");
        context.Log($"Web indirme hazirlandi: {selector} -> {targetPath}");
        return Task.FromResult(new Dictionary<string, object?> { ["path"] = targetPath });
    }

    public ActivityMetadata GetMetadata() => WebActivityHelpers.Metadata(
        "Web.Download",
        "Web Indir",
        "Tetiklenen indirmeyi kaydeder.",
        new()
        {
            new ActivityParameter { Name = "selector", Type = "string", Required = true },
            new ActivityParameter { Name = "targetPath", Type = "string", Required = true },
        },
        new() { new ActivityParameter { Name = "path", Type = "string", Required = true } });
}

public sealed class WebUploadActivity : IActivity
{
    public Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var selector = context.GetVariable<string>("selector");
        var filePath = context.GetVariable<string>("filePath");
        WebActivityHelpers.Require(selector, "selector");
        WebActivityHelpers.Require(filePath, "filePath");
        context.Log($"Web yukleme hazirlandi: {selector} <- {filePath}");
        return Task.FromResult(new Dictionary<string, object?> { ["filePath"] = filePath });
    }

    public ActivityMetadata GetMetadata() => WebActivityHelpers.Metadata(
        "Web.Upload",
        "Web Yukle",
        "Dosya input'una dosya yukler.",
        new()
        {
            new ActivityParameter { Name = "selector", Type = "string", Required = true },
            new ActivityParameter { Name = "filePath", Type = "string", Required = true },
        });
}

public sealed class WebScreenshotActivity : IActivity
{
    private readonly IWebAutomationSessionManager _sessionManager;

    public WebScreenshotActivity(IWebAutomationSessionManager sessionManager)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
    }

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var session = WebActivityHelpers.ReadSessionId(context);
        var selector = context.GetVariable<string>("selector");
        var path = context.GetVariable<string>("path");
        WebActivityHelpers.Require(path, "path");
        var screenshotPath = NormalizeScreenshotPath(path);
        await _sessionManager.ScreenshotAsync(session, string.IsNullOrWhiteSpace(selector) ? null : selector, screenshotPath);
        context.Log(string.IsNullOrWhiteSpace(selector)
            ? $"Web ekran goruntusu kaydedildi: {screenshotPath}"
            : $"Web element ekran goruntusu kaydedildi: {selector} -> {screenshotPath}");
        return new Dictionary<string, object?> { ["path"] = screenshotPath };
    }

    public ActivityMetadata GetMetadata() => WebActivityHelpers.Metadata(
        "Web.Screenshot",
        "Web Ekran Goruntusu",
        "Sayfa/element goruntusu alir.",
        new()
        {
            new ActivityParameter { Name = "selector", Type = "string", Required = false },
            new ActivityParameter { Name = "path", Type = "string", Required = true },
        },
        new() { new ActivityParameter { Name = "path", Type = "string", Required = true } });

    private static string NormalizeScreenshotPath(string path)
    {
        var normalized = path.Trim();
        var extension = Path.GetExtension(normalized);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return normalized + ".png";
        }

        if (IsSupportedScreenshotExtension(extension))
        {
            return normalized;
        }

        throw new BusinessException("Web ekran goruntusu yolu .png, .jpg veya .jpeg ile bitmelidir.");
    }

    private static bool IsSupportedScreenshotExtension(string extension) =>
        extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
}

public sealed class WebFrameSwitchActivity : IActivity
{
    public Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var frameSelector = context.GetVariable<string>("frameSelector");
        var tabIndex = context.GetVariable<int?>("tabIndex");
        var session = WebActivityHelpers.ReadSessionId(context);
        context.Log("Web frame/sekme gecisi hazirlandi.");
        return Task.FromResult(new Dictionary<string, object?> { ["session"] = session });
    }

    public ActivityMetadata GetMetadata() => WebActivityHelpers.Metadata(
        "Web.FrameSwitch",
        "Frame/Sekme Gec",
        "iframe veya sekme baglamina gecer.",
        new()
        {
            new ActivityParameter { Name = "frameSelector", Type = "string", Required = false },
            new ActivityParameter { Name = "tabIndex", Type = "int", Required = false },
        },
        new() { new ActivityParameter { Name = "session", Type = "JSON", Required = true } });
}

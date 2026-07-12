namespace RPA.Agent.Desktop;

using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using Microsoft.Extensions.Logging;
using RPA.Domain.Interfaces;
using Capture = FlaUI.Core.Capturing.Capture;
using SystemException = RPA.Domain.Exceptions.SystemException;

/// <summary>
/// <see cref="IDesktopAutomationChannel"/>'ın FlaUI (UIA3) implementasyonu — herhangi bir Windows
/// masaüstü uygulaması üzerinde otomasyon (Spec Bölüm 5, Paket E). Ajan (robot) sürecinde yaşar.
///
/// <para>Selector formatı UIA yolu: <c>/</c> ile ayrılmış segmentler; her segment isteğe bağlı
/// <c>ControlType</c> adı ve <c>[Key='Value']</c> (tam eşleşme) / <c>[Key~'regex']</c> (regex)
/// koşulları içerir. Desteklenen anahtarlar: <c>AutomationId</c>, <c>Name</c>, <c>Title</c>
/// (Name eşdeğeri). Örn:
/// <c>Window[Title~'Hesap.*']/Pane/Edit[AutomationId='amount']</c>.</para>
///
/// <para>Element bulunamadı / timeout → <see cref="SystemException"/> (teknik hata).</para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class FlaUiDesktopAutomationChannel : IDesktopAutomationChannel, IDisposable
{
    private static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(10);

    private readonly ILogger<FlaUiDesktopAutomationChannel> _logger;
    private readonly UIA3Automation _automation = new();
    private Application? _app;
    private Window? _window;

    public FlaUiDesktopAutomationChannel(ILogger<FlaUiDesktopAutomationChannel> logger)
        => _logger = logger;

    public Task<string> AttachAsync(string? processName, string? windowTitle)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(processName))
            {
                _app = Application.Attach(processName);
            }
            else if (!string.IsNullOrWhiteSpace(windowTitle))
            {
                var proc = System.Diagnostics.Process.GetProcesses()
                    .FirstOrDefault(p => !string.IsNullOrEmpty(p.MainWindowTitle)
                        && Regex.IsMatch(p.MainWindowTitle, windowTitle));
                if (proc is null)
                {
                    throw new SystemException($"Pencere başlığı eşleşmedi: {windowTitle}");
                }
                _app = Application.Attach(proc);
            }
            else
            {
                throw new SystemException("Attach için processName veya windowTitle gerekli.");
            }

            _window = _app.GetMainWindow(_automation);
            return Task.FromResult(DescribeWindow());
        }
        catch (Exception ex) when (ex is not SystemException)
        {
            throw new SystemException($"Uygulamaya bağlanılamadı: {ex.Message}", ex);
        }
    }

    public Task<string> LaunchAsync(string path, string? arguments, bool waitForIdle)
    {
        try
        {
            // Launch öncesi mevcut üst-düzey pencere handle'larını topla. Bazı uygulamalar
            // (calc.exe gibi UWP başlatıcı stub'ları) farklı bir süreçte açılır ve başlatılan
            // PID hemen ölür; bu yüzden PID izlemek yerine "yeni açılan pencereyi" tespit ederiz.
            var before = TopLevelWindowHandles();

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                Arguments = arguments ?? string.Empty,
                UseShellExecute = true,
            };
            var started = System.Diagnostics.Process.Start(psi);

            var window = WaitForNewWindow(before, waitForIdle ? DefaultWait : TimeSpan.FromSeconds(3));

            // Doğrudan başlatılan süreç hâlâ yaşıyorsa ve yeni pencere bulunamadıysa klasik yolu dene.
            if (window is null && started is not null && !started.HasExited)
            {
                try
                {
                    _app = Application.Attach(started);
                    window = _app.GetMainWindow(_automation);
                }
                catch { /* aşağıda genel hata fırlatılır */ }
            }

            if (window is null)
            {
                throw new SystemException($"Uygulama başlatıldı ama pencere bulunamadı ({path}).");
            }

            _window = window;
            _app ??= TryAttach(window);
            return Task.FromResult(DescribeWindow());
        }
        catch (Exception ex) when (ex is not SystemException)
        {
            throw new SystemException($"Uygulama başlatılamadı ({path}): {ex.Message}", ex);
        }
    }

    private HashSet<IntPtr> TopLevelWindowHandles()
    {
        try
        {
            return _automation.GetDesktop().FindAllChildren()
                .Select(w => { try { return w.Properties.NativeWindowHandle.ValueOrDefault; } catch { return IntPtr.Zero; } })
                .Where(h => h != IntPtr.Zero)
                .ToHashSet();
        }
        catch
        {
            return new HashSet<IntPtr>();
        }
    }

    private Window? WaitForNewWindow(HashSet<IntPtr> before, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            foreach (var child in _automation.GetDesktop().FindAllChildren())
            {
                IntPtr handle;
                string title;
                try
                {
                    handle = child.Properties.NativeWindowHandle.ValueOrDefault;
                    title = child.Name ?? string.Empty;
                }
                catch { continue; }

                if (handle != IntPtr.Zero && !before.Contains(handle) && !string.IsNullOrWhiteSpace(title))
                {
                    return child.AsWindow();
                }
            }
            Thread.Sleep(150);
        }
        return null;
    }

    private Application? TryAttach(Window window)
    {
        try
        {
            var pid = window.Properties.ProcessId.ValueOrDefault;
            return pid > 0 ? Application.Attach(pid) : null;
        }
        catch
        {
            return null;
        }
    }

    public Task ClickAsync(string selector, string? clickType)
    {
        var element = Resolve(selector);
        switch ((clickType ?? "left").ToLowerInvariant())
        {
            case "right":
                element.RightClick();
                break;
            case "double":
                element.DoubleClick();
                break;
            default:
                element.Click();
                break;
        }
        return Task.CompletedTask;
    }

    public Task SetTextAsync(string selector, string text)
    {
        var element = Resolve(selector);
        var textBox = element.AsTextBox();
        textBox.Text = text;
        return Task.CompletedTask;
    }

    public Task<string> GetTextAsync(string selector)
    {
        var element = Resolve(selector);
        var value = element.Patterns.Value.PatternOrDefault?.Value?.ValueOrDefault
                    ?? element.Name
                    ?? string.Empty;
        return Task.FromResult(value);
    }

    public Task SelectItemAsync(string selector, string item)
    {
        var element = Resolve(selector);
        element.AsComboBox().Select(item);
        return Task.CompletedTask;
    }

    public Task SendKeysAsync(string? selector, string keys)
    {
        if (!string.IsNullOrWhiteSpace(selector))
        {
            Resolve(selector).Focus();
        }
        Keyboard.Type(keys);
        return Task.CompletedTask;
    }

    public Task WaitForAsync(string selector, int timeoutMs)
    {
        var timeout = timeoutMs > 0 ? TimeSpan.FromMilliseconds(timeoutMs) : DefaultWait;
        var found = Retry.WhileNull(
            () => TryResolve(selector),
            timeout,
            throwOnTimeout: false).Result;

        if (found is null)
        {
            throw new SystemException($"Element {timeoutMs} ms içinde bulunamadı: {selector}");
        }
        return Task.CompletedTask;
    }

    public Task<string> ScreenshotAsync(string? selector)
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), $"desktop-{Guid.NewGuid():N}.png");
            var image = string.IsNullOrWhiteSpace(selector)
                ? (_window is not null ? Capture.Element(_window) : Capture.Screen())
                : Capture.Element(Resolve(selector));
            image.ToFile(path);
            return Task.FromResult(path);
        }
        catch (Exception ex) when (ex is not SystemException)
        {
            throw new SystemException($"Ekran görüntüsü alınamadı: {ex.Message}", ex);
        }
    }

    // ---- Selector çözümleme ----

    private AutomationElement Resolve(string selector)
    {
        var element = TryResolve(selector)
            ?? throw new SystemException($"Element bulunamadı: {selector}");
        return element;
    }

    private AutomationElement? TryResolve(string selector)
    {
        if (_window is null)
        {
            throw new SystemException("Önce Desktop.Attach/Desktop.Launch ile bir pencereye bağlanın.");
        }

        AutomationElement scope = _window;
        foreach (var raw in selector.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var segment = ParseSegment(raw);
            var next = FindBySegment(scope, segment);
            if (next is null)
            {
                return null;
            }
            scope = next;
        }
        return ReferenceEquals(scope, _window) ? null : scope;
    }

    private AutomationElement? FindBySegment(AutomationElement scope, Segment segment)
    {
        var condition = BuildCondition(segment);
        // Koşulsuz segment (ör. yalnız yapısal ara düğüm) → kapsam korunur.
        if (condition is null)
        {
            return scope;
        }

        // Regex koşulları FlaUI ConditionFactory ile ifade edilemez → aday listesi filtrelenir.
        if (segment.Regexes.Count > 0)
        {
            var candidates = scope.FindAllDescendants(condition);
            return candidates.FirstOrDefault(c => segment.Regexes.All(r => MatchRegex(c, r.Key, r.Value)));
        }
        return scope.FindFirstDescendant(condition);
    }

    private ConditionBase? BuildCondition(Segment segment)
    {
        var cf = _automation.ConditionFactory;
        var conditions = new List<ConditionBase>();

        if (segment.ControlType is ControlType ct)
        {
            conditions.Add(cf.ByControlType(ct));
        }
        foreach (var (key, value) in segment.Exact)
        {
            conditions.Add(key.ToLowerInvariant() switch
            {
                "automationid" => cf.ByAutomationId(value),
                "name" or "title" => cf.ByName(value),
                "classname" => cf.ByClassName(value),
                _ => cf.ByName(value),
            });
        }

        if (conditions.Count == 0)
        {
            return null;
        }
        ConditionBase combined = conditions[0];
        for (var i = 1; i < conditions.Count; i++)
        {
            combined = combined.And(conditions[i]);
        }
        return combined;
    }

    private static bool MatchRegex(AutomationElement element, string key, string pattern)
    {
        var text = key.ToLowerInvariant() switch
        {
            "automationid" => element.AutomationId,
            "classname" => element.ClassName,
            _ => element.Name,
        };
        return !string.IsNullOrEmpty(text) && Regex.IsMatch(text, pattern);
    }

    private static Segment ParseSegment(string raw)
    {
        var segment = new Segment();
        var bracket = raw.IndexOf('[');
        var typeName = (bracket < 0 ? raw : raw[..bracket]).Trim();
        if (!string.IsNullOrEmpty(typeName) && Enum.TryParse<ControlType>(typeName, ignoreCase: true, out var ct))
        {
            segment.ControlType = ct;
        }

        if (bracket >= 0)
        {
            foreach (Match m in Regex.Matches(raw, @"\[(?<k>[A-Za-z]+)(?<op>~|=)'(?<v>[^']*)'\]"))
            {
                var key = m.Groups["k"].Value;
                var val = m.Groups["v"].Value;
                if (m.Groups["op"].Value == "~")
                {
                    segment.Regexes.Add(new(key, val));
                }
                else
                {
                    segment.Exact.Add(new(key, val));
                }
            }
        }
        return segment;
    }

    private string DescribeWindow()
        => _window is null ? string.Empty : $"{_window.Properties.ProcessId.ValueOrDefault}:{_window.Title}";

    private sealed class Segment
    {
        public ControlType? ControlType { get; set; }
        public List<KeyValuePair<string, string>> Exact { get; } = new();
        public List<KeyValuePair<string, string>> Regexes { get; } = new();
    }

    public void Dispose()
    {
        _automation.Dispose();
        _app?.Dispose();
    }
}

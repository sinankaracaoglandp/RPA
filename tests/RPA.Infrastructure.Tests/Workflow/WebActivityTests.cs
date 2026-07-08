namespace RPA.Infrastructure.Tests.Workflow;

using Newtonsoft.Json.Linq;
using RPA.Infrastructure.Tests;
using RPA.Infrastructure.Workflow.Activities.Web;
using Xunit;

public sealed class WebActivityTests
{
    [Fact]
    public async Task WebOpen_StartsVisibleChromiumSession()
    {
        var manager = new RecordingWebAutomationSessionManager();
        var activity = new WebOpenActivity(manager);
        var context = new TestActivityExecutionContext();
        context.SetVariable("browser", "chromium");
        context.SetVariable("headless", false);

        var output = await activity.ExecuteAsync(context);

        Assert.Equal("session-1", output["session"]);
        Assert.Equal("chromium", manager.OpenedBrowser);
        Assert.False(manager.OpenedHeadless);
    }

    [Fact]
    public async Task WebGoto_NavigatesExistingSessionToNormalizedUrl()
    {
        var manager = new RecordingWebAutomationSessionManager();
        var activity = new WebGotoActivity(manager);
        var context = new TestActivityExecutionContext();
        context.SetVariable("session", "session-1");
        context.SetVariable("url", "www.google.com");

        var output = await activity.ExecuteAsync(context);

        Assert.Equal("session-1", manager.GotoSessionId);
        Assert.Equal("https://www.google.com/", manager.GotoUrl);
        Assert.Equal("https://www.google.com/", output["url"]);
    }

    [Fact]
    public async Task WebFill_FillsSelectorInExistingSession()
    {
        var manager = new RecordingWebAutomationSessionManager();
        var activity = new WebFillActivity(manager);
        var context = new TestActivityExecutionContext();
        context.SetVariable("session", "session-1");
        context.SetVariable("selector", "input[name=q]");
        context.SetVariable("value", "rpa test");

        await activity.ExecuteAsync(context);

        Assert.Equal("session-1", manager.FillSessionId);
        Assert.Equal("input[name=q]", manager.FillSelector);
        Assert.Equal("rpa test", manager.FillValue);
    }

    [Fact]
    public async Task WebClick_ClicksSelectorInExistingSession()
    {
        var manager = new RecordingWebAutomationSessionManager();
        var activity = new WebClickActivity(manager);
        var context = new TestActivityExecutionContext();
        context.SetVariable("session", "session-1");
        context.SetVariable("selector", "button[type=submit]");

        await activity.ExecuteAsync(context);

        Assert.Equal("session-1", manager.ClickSessionId);
        Assert.Equal("button[type=submit]", manager.ClickSelector);
        Assert.Equal("click", manager.ClickOptions?.Action);
    }

    [Fact]
    public async Task WebClick_WaitsForSubMenuSelectorAfterClick()
    {
        var manager = new RecordingWebAutomationSessionManager();
        var activity = new WebClickActivity(manager);
        var context = new TestActivityExecutionContext();
        context.SetVariable("session", "session-1");
        context.SetVariable("selector", "#top-menu");
        context.SetVariable("waitSelector", "#top-menu .submenu-item");
        context.SetVariable("timeoutMs", 7000);

        await activity.ExecuteAsync(context);

        Assert.Equal("session-1", manager.ClickSessionId);
        Assert.Equal("#top-menu", manager.ClickSelector);
        Assert.Equal("#top-menu .submenu-item", manager.ClickOptions?.WaitSelector);
        Assert.Equal(7000, manager.ClickOptions?.TimeoutMs);
    }

    [Fact]
    public async Task WebClick_CanHoverMenuInsteadOfClicking()
    {
        var manager = new RecordingWebAutomationSessionManager();
        var activity = new WebClickActivity(manager);
        var context = new TestActivityExecutionContext();
        context.SetVariable("session", "session-1");
        context.SetVariable("selector", "#top-menu");
        context.SetVariable("action", "hover");

        await activity.ExecuteAsync(context);

        Assert.Equal("hover", manager.ClickOptions?.Action);
    }

    [Fact]
    public async Task WebClick_ExecutesConfiguredStepsInOrder()
    {
        var manager = new RecordingWebAutomationSessionManager();
        var activity = new WebClickActivity(manager);
        var context = new TestActivityExecutionContext();
        context.SetVariable("session", "session-1");
        context.SetVariable(
            "steps",
            new List<Dictionary<string, object?>>
            {
                new()
                {
                    ["selector"] = "#top-menu",
                    ["action"] = "hover",
                    ["waitSelector"] = "#top-menu .submenu",
                    ["timeoutMs"] = 7000,
                },
                new()
                {
                    ["selector"] = "#top-menu .submenu a.settings",
                    ["action"] = "click",
                    ["timeoutMs"] = 5000,
                },
            });

        var output = await activity.ExecuteAsync(context);

        Assert.Equal(2, manager.Clicks.Count);
        Assert.Equal("#top-menu", manager.Clicks[0].Selector);
        Assert.Equal("hover", manager.Clicks[0].Options.Action);
        Assert.Equal("#top-menu .submenu", manager.Clicks[0].Options.WaitSelector);
        Assert.Equal(7000, manager.Clicks[0].Options.TimeoutMs);
        Assert.Equal("#top-menu .submenu a.settings", manager.Clicks[1].Selector);
        Assert.Equal("click", manager.Clicks[1].Options.Action);
        Assert.Equal(2, output["stepCount"]);
    }

    [Fact]
    public async Task WebClick_ExecutesJArrayStepsInOrder()
    {
        var manager = new RecordingWebAutomationSessionManager();
        var activity = new WebClickActivity(manager);
        var context = new TestActivityExecutionContext();
        context.SetVariable("session", "session-1");
        context.SetVariable(
            "steps",
            new JArray(
                new JObject
                {
                    ["selector"] = "#top-menu",
                    ["action"] = "hover",
                    ["waitSelector"] = "#top-menu .submenu",
                    ["timeoutMs"] = 7000,
                },
                new JObject
                {
                    ["selector"] = "#top-menu .submenu a.settings",
                    ["action"] = "click",
                    ["timeoutMs"] = 5000,
                }));

        var output = await activity.ExecuteAsync(context);

        Assert.Equal(2, manager.Clicks.Count);
        Assert.Equal("#top-menu", manager.Clicks[0].Selector);
        Assert.Equal("#top-menu .submenu a.settings", manager.Clicks[1].Selector);
        Assert.Equal(2, output["stepCount"]);
    }

    [Fact]
    public async Task WebGetText_ReadsTextFromExistingSession()
    {
        var manager = new RecordingWebAutomationSessionManager { Text = "hello page" };
        var activity = new WebGetTextActivity(manager);
        var context = new TestActivityExecutionContext();
        context.SetVariable("session", "session-1");
        context.SetVariable("selector", ".message");
        context.SetVariable("outputVariable", "okunanMetin");

        var output = await activity.ExecuteAsync(context);

        Assert.Equal("session-1", manager.GetTextSessionId);
        Assert.Equal(".message", manager.GetTextSelector);
        Assert.Equal("hello page", output["text"]);
        Assert.Equal("hello page", output["okunanMetin"]);
        Assert.Equal("hello page", context.GetVariable<string>("text"));
        Assert.Equal("hello page", context.GetVariable<string>("okunanMetin"));
    }

    [Fact]
    public async Task WebWaitFor_WaitsForSelectorInExistingSession()
    {
        var manager = new RecordingWebAutomationSessionManager();
        var activity = new WebWaitForActivity(manager);
        var context = new TestActivityExecutionContext();
        context.SetVariable("session", "session-1");
        context.SetVariable("selector", "#ready");
        context.SetVariable("timeoutMs", 5000);

        await activity.ExecuteAsync(context);

        Assert.Equal("session-1", manager.WaitForSessionId);
        Assert.Equal("#ready", manager.WaitForSelector);
        Assert.Equal(5000, manager.WaitForTimeoutMs);
    }

    [Fact]
    public async Task WebScreenshot_SavesPageOrElementToRequestedPath()
    {
        var manager = new RecordingWebAutomationSessionManager();
        var activity = new WebScreenshotActivity(manager);
        var context = new TestActivityExecutionContext();
        context.SetVariable("session", "session-1");
        context.SetVariable("selector", "#invoice");
        context.SetVariable("path", @"C:\Temp\invoice.png");

        var output = await activity.ExecuteAsync(context);

        Assert.Equal("session-1", manager.ScreenshotSessionId);
        Assert.Equal("#invoice", manager.ScreenshotSelector);
        Assert.Equal(@"C:\Temp\invoice.png", manager.ScreenshotPath);
        Assert.Equal(@"C:\Temp\invoice.png", output["path"]);
    }

    [Fact]
    public async Task WebScreenshot_AppendsPngExtension_WhenPathHasNoExtension()
    {
        var manager = new RecordingWebAutomationSessionManager();
        var activity = new WebScreenshotActivity(manager);
        var context = new TestActivityExecutionContext();
        context.SetVariable("session", "session-1");
        context.SetVariable("path", @"C:\Temp\invoice");

        var output = await activity.ExecuteAsync(context);

        Assert.Equal(@"C:\Temp\invoice.png", manager.ScreenshotPath);
        Assert.Equal(@"C:\Temp\invoice.png", output["path"]);
    }

    private sealed class RecordingWebAutomationSessionManager : IWebAutomationSessionManager
    {
        public string? OpenedBrowser { get; private set; }
        public bool? OpenedHeadless { get; private set; }
        public string? GotoSessionId { get; private set; }
        public string? GotoUrl { get; private set; }
        public string? FillSessionId { get; private set; }
        public string? FillSelector { get; private set; }
        public string? FillValue { get; private set; }
        public string? ClickSessionId { get; private set; }
        public string? ClickSelector { get; private set; }
        public WebClickOptions? ClickOptions { get; private set; }
        public List<RecordedClick> Clicks { get; } = new();
        public string? GetTextSessionId { get; private set; }
        public string? GetTextSelector { get; private set; }
        public string? WaitForSessionId { get; private set; }
        public string? WaitForSelector { get; private set; }
        public int? WaitForTimeoutMs { get; private set; }
        public string? ScreenshotSessionId { get; private set; }
        public string? ScreenshotSelector { get; private set; }
        public string? ScreenshotPath { get; private set; }
        public string Text { get; set; } = string.Empty;

        public Task<string> OpenAsync(string browser, bool headless, CancellationToken cancellationToken = default)
        {
            OpenedBrowser = browser;
            OpenedHeadless = headless;
            return Task.FromResult("session-1");
        }

        public Task GotoAsync(string sessionId, Uri uri, CancellationToken cancellationToken = default)
        {
            GotoSessionId = sessionId;
            GotoUrl = uri.AbsoluteUri;
            return Task.CompletedTask;
        }

        public Task FillAsync(string sessionId, string selector, string value, CancellationToken cancellationToken = default)
        {
            FillSessionId = sessionId;
            FillSelector = selector;
            FillValue = value;
            return Task.CompletedTask;
        }

        public Task ClickAsync(string sessionId, string selector, WebClickOptions options, CancellationToken cancellationToken = default)
        {
            ClickSessionId = sessionId;
            ClickSelector = selector;
            ClickOptions = options;
            Clicks.Add(new RecordedClick(sessionId, selector, options));
            return Task.CompletedTask;
        }

        public Task<string> GetTextAsync(string sessionId, string selector, CancellationToken cancellationToken = default)
        {
            GetTextSessionId = sessionId;
            GetTextSelector = selector;
            return Task.FromResult(Text);
        }

        public Task WaitForAsync(string sessionId, string selector, int timeoutMs, CancellationToken cancellationToken = default)
        {
            WaitForSessionId = sessionId;
            WaitForSelector = selector;
            WaitForTimeoutMs = timeoutMs;
            return Task.CompletedTask;
        }

        public Task ScreenshotAsync(string sessionId, string? selector, string path, CancellationToken cancellationToken = default)
        {
            ScreenshotSessionId = sessionId;
            ScreenshotSelector = selector;
            ScreenshotPath = path;
            return Task.CompletedTask;
        }

        public sealed record RecordedClick(string SessionId, string Selector, WebClickOptions Options);
    }
}

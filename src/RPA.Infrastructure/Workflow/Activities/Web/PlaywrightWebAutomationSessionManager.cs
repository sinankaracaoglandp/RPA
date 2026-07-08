namespace RPA.Infrastructure.Workflow.Activities.Web;

using System.Collections.Concurrent;
using Microsoft.Playwright;
using BusinessException = RPA.Domain.Exceptions.BusinessException;

public sealed class PlaywrightWebAutomationSessionManager : IWebAutomationSessionManager, IDisposable, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, WebAutomationSession> _sessions = new();
    private readonly SemaphoreSlim _playwrightLock = new(1, 1);
    private IPlaywright? _playwright;

    public async Task<string> OpenAsync(string browser, bool headless, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var playwright = await GetPlaywrightAsync();
        var options = BuildLaunchOptions(browser, headless);
        var launchedBrowser = await playwright.Chromium.LaunchAsync(options);
        var page = await launchedBrowser.NewPageAsync();
        var sessionId = Guid.NewGuid().ToString("N");

        _sessions[sessionId] = new WebAutomationSession(launchedBrowser, page);
        return sessionId;
    }

    public async Task GotoAsync(string sessionId, Uri uri, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = GetSession(sessionId);
        await session.Page.GotoAsync(uri.AbsoluteUri);
    }

    public async Task FillAsync(string sessionId, string selector, string value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = GetSession(sessionId);
        await session.Page.FillAsync(selector, value);
    }

    public async Task ClickAsync(string sessionId, string selector, WebClickOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = GetSession(sessionId);
        var action = string.IsNullOrWhiteSpace(options.Action)
            ? "click"
            : options.Action.Trim().ToLowerInvariant();

        switch (action)
        {
            case "click":
                await session.Page.ClickAsync(selector);
                break;
            case "hover":
                await session.Page.HoverAsync(selector);
                break;
            default:
                throw new BusinessException($"Desteklenmeyen web tiklama aksiyonu: {options.Action}. Desteklenen degerler: click, hover.");
        }

        if (!string.IsNullOrWhiteSpace(options.WaitSelector))
        {
            await session.Page.WaitForSelectorAsync(
                options.WaitSelector,
                new PageWaitForSelectorOptions { Timeout = options.TimeoutMs });
        }
    }

    public async Task<string> GetTextAsync(string sessionId, string selector, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = GetSession(sessionId);
        return await session.Page.InnerTextAsync(selector);
    }

    public async Task WaitForAsync(string sessionId, string selector, int timeoutMs, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = GetSession(sessionId);
        await session.Page.WaitForSelectorAsync(selector, new PageWaitForSelectorOptions { Timeout = timeoutMs });
    }

    public async Task ScreenshotAsync(string sessionId, string? selector, string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = GetSession(sessionId);
        path = NormalizeScreenshotPath(path);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (string.IsNullOrWhiteSpace(selector))
        {
            await session.Page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = true });
            return;
        }

        await session.Page.Locator(selector).ScreenshotAsync(new LocatorScreenshotOptions { Path = path });
    }

    private static string NormalizeScreenshotPath(string path)
    {
        var normalized = path.Trim();
        var extension = Path.GetExtension(normalized);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return normalized + ".png";
        }

        if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        throw new BusinessException("Web ekran goruntusu yolu .png, .jpg veya .jpeg ile bitmelidir.");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in _sessions.Values)
        {
            await session.Browser.CloseAsync();
        }

        _playwright?.Dispose();
        _playwrightLock.Dispose();
    }

    public void Dispose()
    {
        foreach (var session in _sessions.Values)
        {
            session.Browser.CloseAsync().GetAwaiter().GetResult();
        }

        _playwright?.Dispose();
        _playwrightLock.Dispose();
    }

    private async Task<IPlaywright> GetPlaywrightAsync()
    {
        if (_playwright is not null)
        {
            return _playwright;
        }

        await _playwrightLock.WaitAsync();
        try
        {
            _playwright ??= await Playwright.CreateAsync();
            return _playwright;
        }
        finally
        {
            _playwrightLock.Release();
        }
    }

    private static BrowserTypeLaunchOptions BuildLaunchOptions(string browser, bool headless)
    {
        var normalized = string.IsNullOrWhiteSpace(browser)
            ? "chromium"
            : browser.Trim().ToLowerInvariant();

        var options = new BrowserTypeLaunchOptions { Headless = headless };

        switch (normalized)
        {
            case "chromium":
                return options;
            case "chrome":
            case "google-chrome":
                options.Channel = "chrome";
                return options;
            case "edge":
            case "msedge":
            case "microsoft-edge":
                options.Channel = "msedge";
                return options;
            default:
                throw new BusinessException($"Desteklenmeyen browser: {browser}. Desteklenen degerler: chromium, chrome, edge.");
        }
    }

    private WebAutomationSession GetSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || !_sessions.TryGetValue(sessionId, out var session))
        {
            throw new BusinessException("Web session bulunamadi. Once Web.Open aktivitesini calistirin.");
        }

        return session;
    }

    private sealed record WebAutomationSession(IBrowser Browser, IPage Page);
}

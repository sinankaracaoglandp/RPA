namespace RPA.Infrastructure.Workflow.Activities.Web;

public interface IWebAutomationSessionManager
{
    Task<string> OpenAsync(string browser, bool headless, CancellationToken cancellationToken = default);

    Task GotoAsync(string sessionId, Uri uri, CancellationToken cancellationToken = default);

    Task FillAsync(string sessionId, string selector, string value, CancellationToken cancellationToken = default);

    Task ClickAsync(string sessionId, string selector, WebClickOptions options, CancellationToken cancellationToken = default);

    Task<string> GetTextAsync(string sessionId, string selector, CancellationToken cancellationToken = default);

    Task WaitForAsync(string sessionId, string selector, int timeoutMs, CancellationToken cancellationToken = default);

    Task ScreenshotAsync(string sessionId, string? selector, string path, CancellationToken cancellationToken = default);
}

public sealed record WebClickOptions(string Action = "click", string? WaitSelector = null, int TimeoutMs = 30000);

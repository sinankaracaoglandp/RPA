namespace RPA.Infrastructure.Activities.Desktop;

using RPA.Domain.Interfaces;
using RPA.Domain.ValueObjects;

/// <summary>
/// Placeholder channel used by non-agent processes so Desktop.* activity registrations
/// remain DI-valid. The real FlaUI implementation is registered by RPA.Agent on Windows.
/// </summary>
public sealed class UnavailableDesktopAutomationChannel : IDesktopAutomationChannel
{
    public Task<string> AttachAsync(string? processName, string? windowTitle) => Unavailable<string>();

    public Task<string> LaunchAsync(string path, string? arguments, bool waitForIdle) => Unavailable<string>();

    public Task ClickAsync(string selector, string? clickType) => Unavailable();

    public Task SetTextAsync(string selector, string text) => Unavailable();

    public Task<string> GetTextAsync(string selector) => Unavailable<string>();

    public Task SelectItemAsync(string selector, string item) => Unavailable();

    public Task SendKeysAsync(string? selector, string keys) => Unavailable();

    public Task SendKeysAsync(string? selector, IReadOnlyList<KeystrokeStep> steps) => Unavailable();

    public Task WaitForAsync(string selector, int timeoutMs) => Unavailable();

    public Task<string> ScreenshotAsync(string? selector) => Unavailable<string>();

    private static Task Unavailable() =>
        Task.FromException(new InvalidOperationException(Message));

    private static Task<T> Unavailable<T>() =>
        Task.FromException<T>(new InvalidOperationException(Message));

    private const string Message =
        "Desktop automation channel is not available in this process. Run Desktop.* activities through RPA.Agent on Windows.";
}

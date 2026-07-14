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
    public Task ClickTextOffsetAsync(string anchorText, int dx, int dy,
        string language, string matchMode, string? clickType, int timeoutMs)
        => Unavailable();
    public Task<bool> TextExistsAsync(string text, string language, string matchMode, int timeoutMs) => Unavailable<bool>();

    private static Task Unavailable() => Task.FromException(new InvalidOperationException(Message));
    private static Task<T> Unavailable<T>() => Task.FromException<T>(new InvalidOperationException(Message));

    private const string Message =
        "Vision automation channel is not available in this process. Run Vision.* activities through RPA.Agent on Windows.";
}

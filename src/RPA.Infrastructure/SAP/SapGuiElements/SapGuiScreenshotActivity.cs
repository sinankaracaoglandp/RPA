namespace RPA.Infrastructure.SAP.SapGuiElements;

using System.Runtime.Versioning;
using RPA.Domain.Interfaces;

/// <summary>
/// Aktif SAP GUI ekranının görüntüsünü alır (Spec 5.3 — SAP GUI: Screenshot).
/// İş bitiminde/hata analizinde arşivlenmek üzere PNG byte'ları "screenshot" çıkış
/// değişkenine yazılır.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SapGuiScreenshotActivity : IActivity
{
    private readonly ISapGuiChannel _channel;

    public SapGuiScreenshotActivity(ISapGuiChannel channel)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
    }

    public ActivityMetadata GetMetadata() => new()
    {
        ActivityId = "Sap.Gui.Screenshot",
        DisplayName = "SAP GUI Ekran Görüntüsü",
        Category = "SAP",
        Description = "Aktif SAP GUI ekranının görüntüsünü (PNG) alır.",
        Inputs = new(),
        Outputs = new()
        {
            new ActivityParameter { Name = "screenshot", Type = "bytes", Required = false, Description = "PNG ekran görüntüsü" }
        },
        RequiredCapabilities = new() { "sap-gui" }
    };

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        context.Log("SAP GUI ekran görüntüsü alınıyor.");
        var bytes = await _channel.CaptureScreenAsync();
        context.SetVariable("screenshot", bytes);
        context.Log($"SAP GUI ekran görüntüsü alındı: {bytes.Length} byte.");
        return new() { ["screenshot"] = bytes };
    }
}

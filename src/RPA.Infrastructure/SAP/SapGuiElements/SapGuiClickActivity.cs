namespace RPA.Infrastructure.SAP.SapGuiElements;

using System.Runtime.Versioning;
using RPA.Domain.Interfaces;
using BusinessException = RPA.Domain.Exceptions.BusinessException;

/// <summary>
/// SAP GUI ekranındaki bir elemente tıklar (Spec 5.3 — SAP GUI: Click).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SapGuiClickActivity : IActivity
{
    private readonly ISapGuiChannel _channel;

    public SapGuiClickActivity(ISapGuiChannel channel)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
    }

    public ActivityMetadata GetMetadata() => new()
    {
        ActivityId = "Sap.Gui.Click",
        DisplayName = "SAP GUI Tıkla",
        Category = "SAP",
        Description = "SAP GUI ekranındaki bir elemente tıklar.",
        Inputs = new()
        {
            new ActivityParameter { Name = "elementId", Type = "string", Required = true, Description = "Hiyerarşik element ID (wnd[0]/usr/...)" }
        },
        Outputs = new(),
        RequiredCapabilities = new() { "sap-gui" }
    };

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var elementId = context.GetVariable<string>("elementId");
        if (string.IsNullOrWhiteSpace(elementId))
        {
            throw new BusinessException("'elementId' parametresi boş olamaz.");
        }

        context.Log($"SAP GUI tıklanıyor: {elementId}");
        await _channel.ClickAsync(elementId);
        return new();
    }
}

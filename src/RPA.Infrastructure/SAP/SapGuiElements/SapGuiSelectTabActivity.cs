namespace RPA.Infrastructure.SAP.SapGuiElements;

using RPA.Domain.Interfaces;
using BusinessException = RPA.Domain.Exceptions.BusinessException;

/// <summary>
/// SAP GUI ekranındaki tab-strip'te bir sekme seçer (Spec 5.3 — SAP GUI: SelectTab).
/// </summary>
public sealed class SapGuiSelectTabActivity : IActivity
{
    private readonly ISapGuiChannel _channel;

    public SapGuiSelectTabActivity(ISapGuiChannel channel)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
    }

    public ActivityMetadata GetMetadata() => new()
    {
        ActivityId = "Sap.Gui.SelectTab",
        DisplayName = "SAP GUI Sekme Seç",
        Category = "SAP",
        Description = "SAP GUI tab-strip'te bir sekme seçer.",
        Inputs = new()
        {
            new ActivityParameter { Name = "elementId", Type = "string", Required = true, Description = "Tab element ID (.../tabsTABSTRIP/tabpTAB01)" }
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

        context.Log($"SAP GUI sekme seçiliyor: {elementId}");
        await _channel.SelectTabAsync(elementId);
        return new();
    }
}

namespace RPA.Infrastructure.SAP.SapGuiElements;

using RPA.Domain.Interfaces;
using BusinessException = RPA.Domain.Exceptions.BusinessException;

/// <summary>
/// SAP GUI ekranındaki bir alana metin yazar (Spec 5.3 — SAP GUI: SetText).
/// </summary>
public sealed class SapGuiSetTextActivity : IActivity
{
    private readonly ISapGuiChannel _channel;

    public SapGuiSetTextActivity(ISapGuiChannel channel)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
    }

    public ActivityMetadata GetMetadata() => new()
    {
        ActivityId = "Sap.Gui.SetText",
        DisplayName = "SAP GUI Metin Yaz",
        Category = "SAP",
        Description = "SAP GUI ekranındaki bir alana metin yazar.",
        Inputs = new()
        {
            new ActivityParameter { Name = "elementId", Type = "string", Required = true, Description = "Alan element ID" },
            new ActivityParameter { Name = "text", Type = "string", Required = true, Description = "Yazılacak metin" }
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

        var text = context.GetVariable<string?>("text") ?? string.Empty;

        context.Log($"SAP GUI metin yazılıyor: {elementId}");
        await _channel.SetTextAsync(elementId, text);
        return new();
    }
}

namespace RPA.Infrastructure.SAP.SapGuiElements;

using RPA.Domain.Interfaces;
using BusinessException = RPA.Domain.Exceptions.BusinessException;

/// <summary>
/// SAP GUI ekranındaki bir alandan metin okur (Spec 5.3 — SAP GUI: GetText).
/// Sonuç "text" çıkış değişkenine yazılır.
/// </summary>
public sealed class SapGuiGetTextActivity : IActivity
{
    private readonly ISapGuiChannel _channel;

    public SapGuiGetTextActivity(ISapGuiChannel channel)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
    }

    public ActivityMetadata GetMetadata() => new()
    {
        ActivityId = "Sap.Gui.GetText",
        DisplayName = "SAP GUI Metin Oku",
        Category = "SAP",
        Description = "SAP GUI ekranındaki bir alandan metin okur.",
        Inputs = new()
        {
            new ActivityParameter { Name = "elementId", Type = "string", Required = true, Description = "Alan element ID" }
        },
        Outputs = new()
        {
            new ActivityParameter { Name = "text", Type = "string", Required = false, Description = "Okunan metin" }
        },
        RequiredCapabilities = new() { "sap-gui" }
    };

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var elementId = context.GetVariable<string>("elementId");
        if (string.IsNullOrWhiteSpace(elementId))
        {
            throw new BusinessException("'elementId' parametresi boş olamaz.");
        }

        context.Log($"SAP GUI metin okunuyor: {elementId}");
        var text = await _channel.GetTextAsync(elementId);
        context.SetVariable("text", text);
        return new() { ["text"] = text };
    }
}

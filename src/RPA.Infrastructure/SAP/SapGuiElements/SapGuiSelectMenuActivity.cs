namespace RPA.Infrastructure.SAP.SapGuiElements;

using System.Runtime.Versioning;
using RPA.Domain.Interfaces;
using BusinessException = RPA.Domain.Exceptions.BusinessException;

/// <summary>
/// SAP GUI menü çubuğunda metin yoluyla gezinip bir menü öğesi seçer (örn. "Sistem/Liste/Yazdır").
/// Element ID'lerine bağlı değildir; odak/görünürlükten etkilenmez (COM scripting).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SapGuiSelectMenuActivity : IActivity
{
    private readonly ISapGuiChannel _channel;

    public SapGuiSelectMenuActivity(ISapGuiChannel channel)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
    }

    public ActivityMetadata GetMetadata() => new()
    {
        ActivityId = "Sap.Gui.SelectMenu",
        DisplayName = "SAP GUI Menü Seç",
        Category = "SAP",
        Description = "Menü çubuğunda metin yoluyla gezinip menü öğesi seçer (örn. Sistem/Liste/Yazdır).",
        Inputs = new()
        {
            new ActivityParameter { Name = "menuPath", Type = "string", Required = true, Description = "'/' ile ayrılmış menü metni yolu (örn. Sistem/Liste/Yazdır)." }
        },
        Outputs = new(),
        RequiredCapabilities = new() { "sap-gui" }
    };

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var menuPath = context.GetVariable<string>("menuPath");
        if (string.IsNullOrWhiteSpace(menuPath))
        {
            throw new BusinessException("'menuPath' parametresi boş olamaz.");
        }

        context.Log($"SAP GUI menü seçiliyor: {menuPath}");
        await _channel.SelectMenuAsync(menuPath);
        return new();
    }
}

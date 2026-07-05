namespace RPA.Infrastructure.SAP.SapGuiElements;

using RPA.Domain.Interfaces;
using BusinessException = RPA.Domain.Exceptions.BusinessException;

/// <summary>
/// SAP GUI ALV grid'ini okur (Spec 5.3 — SAP GUI: GridOku (ALV)).
/// Sonuç "rows" çıkış değişkenine (satır listesi) yazılır.
/// </summary>
public sealed class SapGuiGridReadActivity : IActivity
{
    private readonly ISapGuiChannel _channel;

    public SapGuiGridReadActivity(ISapGuiChannel channel)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
    }

    public ActivityMetadata GetMetadata() => new()
    {
        ActivityId = "Sap.Gui.GridRead",
        DisplayName = "SAP GUI Grid Oku",
        Category = "SAP",
        Description = "SAP GUI ALV grid içeriğini satır listesi olarak okur.",
        Inputs = new()
        {
            new ActivityParameter { Name = "gridId", Type = "string", Required = true, Description = "ALV grid element ID" }
        },
        Outputs = new()
        {
            new ActivityParameter { Name = "rows", Type = "JSON", Required = false, Description = "Satır listesi (sütun-değer)" }
        },
        RequiredCapabilities = new() { "sap-gui" }
    };

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var gridId = context.GetVariable<string>("gridId");
        if (string.IsNullOrWhiteSpace(gridId))
        {
            throw new BusinessException("'gridId' parametresi boş olamaz.");
        }

        context.Log($"SAP GUI grid okunuyor: {gridId}");
        var rows = await _channel.ReadGridAsync(gridId);
        context.SetVariable("rows", rows);
        context.Log($"SAP GUI grid okundu: {rows.Count} satır.");
        return new() { ["rows"] = rows };
    }
}

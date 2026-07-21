namespace RPA.Infrastructure.SAP.SapGuiElements;

using System.Runtime.Versioning;
using RPA.Domain.Interfaces;
using BusinessException = RPA.Domain.Exceptions.BusinessException;

/// <summary>
/// SAP GUI ALV grid'ini okur (Spec 5.3 — SAP GUI: GridOku (ALV)).
/// Sonuç "rows" çıkış değişkenine (satır listesi) yazılır.
/// </summary>
[SupportedOSPlatform("windows")]
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
            new ActivityParameter { Name = "gridId", Type = "string", Required = true, Description = "ALV grid element ID" },
            new ActivityParameter
            {
                Name = "columns",
                Type = "JSON",
                Required = false,
                Description = "Tasarım anında okunan teknik kolon adları (JSON dizi). Verilirse satırlar bu sözleşmeye göre şekillenir.",
            },
            new ActivityParameter
            {
                Name = "outputVariable",
                Type = "string",
                Required = false,
                DefaultValue = "gridSatirlari",
                Description = "Satır listesinin atanacağı workflow değişkeni",
            }
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

        // Tasarım anındaki kolon sözleşmesi (picker grid seçildiğinde doldurur).
        var declared = ParseDeclaredColumns(context.GetVariable<object?>("columns"));
        if (declared.Count > 0)
        {
            rows = ProjectToDeclaredColumns(rows, declared);
            context.SetVariable("rows", rows);
        }

        var outputs = new Dictionary<string, object?> { ["rows"] = rows };

        // Kullanıcı bir çıktı değişkeni seçtiyse satır listesini ona da bağla (File.List deseni)
        // → sonraki node'lar (ör. Logic.ForEach) {{degisken}} ile listeye erişebilir.
        var outputVariable = context.GetVariable<string>("outputVariable")?.Trim();
        if (!string.IsNullOrWhiteSpace(outputVariable))
        {
            context.SetVariable(outputVariable, rows);
            outputs[outputVariable] = rows;
        }

        return outputs;
    }

    /// <summary>
    /// Satırları tasarım anındaki kolon sözleşmesine göre şekillendirir:
    /// <list type="bullet">
    /// <item>Tasarımda olup çalışma anında BULUNMAYAN kolon → <c>null</c> (alan yine de vardır,
    /// böylece sonraki node'lardaki ifadeler kırılmaz).</item>
    /// <item>Çalışma anında FAZLADAN gelen kolon → yok sayılır (sözleşme dışıdır).</item>
    /// </list>
    /// </summary>
    private static List<Dictionary<string, object?>> ProjectToDeclaredColumns(
        List<Dictionary<string, object?>> rows,
        IReadOnlyList<string> declaredColumns)
    {
        var projected = new List<Dictionary<string, object?>>(rows.Count);

        foreach (var row in rows)
        {
            var shaped = new Dictionary<string, object?>(declaredColumns.Count);
            foreach (var column in declaredColumns)
            {
                shaped[column] = row.TryGetValue(column, out var value) ? value : null;
            }

            projected.Add(shaped);
        }

        return projected;
    }

    /// <summary>Kolon sözleşmesini JSON dizi ya da hazır liste olarak okur (boş/bozuk → boş liste).</summary>
    private static IReadOnlyList<string> ParseDeclaredColumns(object? raw)
    {
        switch (raw)
        {
            case null:
                return Array.Empty<string>();

            case IEnumerable<string> list:
                return list.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();

            case string json when !string.IsNullOrWhiteSpace(json):
                try
                {
                    return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json)?
                        .Where(c => !string.IsNullOrWhiteSpace(c))
                        .ToList()
                        ?? (IReadOnlyList<string>)Array.Empty<string>();
                }
                catch (System.Text.Json.JsonException)
                {
                    // Kolon sözleşmesi bozuksa sözleşmesiz davran (tüm çalışma-anı kolonları).
                    return Array.Empty<string>();
                }

            default:
                return Array.Empty<string>();
        }
    }
}

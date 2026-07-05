namespace RPA.Infrastructure.SAP.Nco;

using RPA.Domain.Interfaces;
using BusinessException = RPA.Domain.Exceptions.BusinessException;

/// <summary>
/// SAP NCo: RFC_READ_TABLE ile tablo okuma aktivitesi (Task 4.2, Spec 6). "tableName" (zorunlu),
/// "fields" ve "where" değişkenlerini alır; sonuç satırları "rows" çıktısına yazılır.
/// </summary>
public sealed class SapNcoReadTableActivity : IActivity
{
    private readonly ISapDataChannel _channel;

    public SapNcoReadTableActivity(ISapDataChannel channel)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
    }

    public ActivityMetadata GetMetadata() => new()
    {
        ActivityId = "Sap.Nco.ReadTable",
        DisplayName = "SAP Tablo Oku",
        Category = "SAP",
        Description = "Standard RFC_READ_TABLE ile bir SAP tablosunu okur.",
        Inputs = new()
        {
            new ActivityParameter { Name = "tableName", Type = "string", Required = true, Description = "SAP tablo adı (örn. MARA)" },
            new ActivityParameter { Name = "fields", Type = "JSON", Required = false, Description = "İstenen alanlar (null=tümü)" },
            new ActivityParameter { Name = "where", Type = "string", Required = false, Description = "WHERE koşulu" },
        },
        Outputs = new()
        {
            new ActivityParameter { Name = "rows", Type = "JSON", Required = false, Description = "Satır listesi" },
            new ActivityParameter { Name = "rowCount", Type = "int", Required = false, Description = "Satır sayısı" },
        },
        RequiredCapabilities = new() { "sap-nco" },
    };

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var tableName = context.GetVariable<string>("tableName");
        if (string.IsNullOrWhiteSpace(tableName))
        {
            throw new BusinessException("'tableName' parametresi boş olamaz.");
        }

        var fields = context.GetVariable<List<string>>("fields");
        var where = context.GetVariable<string>("where");

        context.Log($"SAP tablo okunuyor: {tableName}");
        var rows = await _channel.ReadTableAsync(tableName, fields, string.IsNullOrWhiteSpace(where) ? null : where);

        context.SetVariable("rows", rows);
        context.SetVariable("rowCount", rows.Count);

        return new()
        {
            ["rows"] = rows,
            ["rowCount"] = rows.Count,
        };
    }
}

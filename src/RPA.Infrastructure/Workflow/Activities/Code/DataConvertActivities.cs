namespace RPA.Infrastructure.Workflow.Activities.Code;

using RPA.Domain.Interfaces;
using BusinessException = RPA.Domain.Exceptions.BusinessException;

/// <summary>
/// Satır listesini (SAP/Excel çıktısı) gerçek <see cref="System.Data.DataTable"/>'a çevirir
/// (<c>Data.ToDataTable</c>). Böylece DataTable, aktiviteler ve C# kod aktivitesi arasında akabilir.
/// </summary>
public sealed class DataToDataTableActivity : IActivity
{
    public ActivityMetadata GetMetadata() => new()
    {
        ActivityId = "Data.ToDataTable",
        DisplayName = "DataTable'a Çevir",
        Category = "Kod & Veri",
        Description = "Satır listesini (sütun-değer) gerçek System.Data.DataTable'a dönüştürür.",
        Inputs = new()
        {
            new ActivityParameter { Name = "rows", Type = "JSON", Required = true, Description = "Satır listesi" },
        },
        Outputs = new()
        {
            new ActivityParameter { Name = "table", Type = "DataTable", Required = false, Description = "DataTable" },
        },
    };

    public Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var rows = context.GetVariable<object?>("rows");
        var table = DataTableConverter.ToDataTable(rows);
        context.SetVariable("table", table);
        context.Log($"DataTable oluşturuldu: {table.Rows.Count} satır, {table.Columns.Count} sütun.");
        return Task.FromResult(new Dictionary<string, object?> { ["table"] = table });
    }
}

/// <summary>
/// <see cref="System.Data.DataTable"/>'ı platformun satır-listesi gösterimine çevirir
/// (<c>Data.FromDataTable</c>) — forEach/JSON/ifade uyumluluğu için.
/// </summary>
public sealed class DataFromDataTableActivity : IActivity
{
    public ActivityMetadata GetMetadata() => new()
    {
        ActivityId = "Data.FromDataTable",
        DisplayName = "DataTable'dan Satırlar",
        Category = "Kod & Veri",
        Description = "DataTable'ı satır listesine (sütun-değer) dönüştürür.",
        Inputs = new()
        {
            new ActivityParameter { Name = "table", Type = "DataTable", Required = true, Description = "DataTable" },
        },
        Outputs = new()
        {
            new ActivityParameter { Name = "rows", Type = "JSON", Required = false, Description = "Satır listesi" },
        },
    };

    public Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var value = context.GetVariable<object?>("table");
        if (value is null)
        {
            throw new BusinessException("'table' parametresi boş olamaz.");
        }

        // DataTable ise doğrudan; satır-listesi verildiyse önce DataTable'a normalize et.
        var table = DataTableConverter.ToDataTable(value);
        var rows = DataTableConverter.ToRows(table);
        context.SetVariable("rows", rows);
        context.Log($"DataTable'dan {rows.Count} satır çıkarıldı.");
        return Task.FromResult(new Dictionary<string, object?> { ["rows"] = rows });
    }
}

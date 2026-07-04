namespace RPA.Infrastructure.Activities.Excel;

using System.Data;
using ClosedXML.Excel;
using RPA.Domain.Interfaces;

/// <summary>
/// Excel dosyasından belirtilen aralık veya tabloyu DataTable olarak okur.
/// Aktivite ID: Excel.Read
/// </summary>
public class ExcelReadActivity : IActivity
{
    public Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        // Giriş parametrelerini al
        string filePath = context.GetVariable<string>("filePath");
        string? sheet = context.GetVariable<string?>("sheet");
        string? range = context.GetVariable<string?>("range");

        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("filePath boş olamaz.");

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Excel dosyası bulunamadı: {filePath}");

        try
        {
            using (var workbook = new XLWorkbook(filePath))
            {
                // Sheet'i belirle
                IXLWorksheet worksheet;
                if (!string.IsNullOrWhiteSpace(sheet))
                {
                    if (!workbook.TryGetWorksheet(sheet, out worksheet!))
                        throw new InvalidOperationException($"Sheet '{sheet}' bulunamadı.");
                }
                else
                {
                    // Varsayılan: ilk sheet
                    worksheet = workbook.Worksheet(1);
                }

                // Range'i belirle veya kullanılan hücreleri al
                IXLRange? dataRange = null;
                if (!string.IsNullOrWhiteSpace(range))
                {
                    dataRange = worksheet.Range(range);
                }
                else
                {
                    // Kullanılan bölgeyi al
                    var usedRange = worksheet.RangeUsed();
                    dataRange = usedRange ?? worksheet.Range("A1:A1");
                }

                // DataTable'a dönüştür
                var dataTable = new DataTable();
                bool headerProcessed = false;

                foreach (var row in dataRange.Rows())
                {
                    if (!headerProcessed)
                    {
                        // Header satırı: sütun adlarını al
                        foreach (var cell in row.Cells())
                        {
                            string colName = cell.Value.ToString() ?? $"Column{dataTable.Columns.Count}";
                            dataTable.Columns.Add(colName);
                        }
                        headerProcessed = true;
                    }
                    else
                    {
                        // Veri satırları
                        var values = new object?[dataTable.Columns.Count];
                        int colIndex = 0;
                        foreach (var cell in row.Cells())
                        {
                            if (colIndex < dataTable.Columns.Count)
                            {
                                values[colIndex] = cell.Value;
                                colIndex++;
                            }
                        }
                        dataTable.Rows.Add(values);
                    }
                }

                context.Log($"Excel dosyası okundu: {filePath}, Sheet: {worksheet.Name}, Satır: {dataTable.Rows.Count}");

                return Task.FromResult(new Dictionary<string, object?> { { "data", dataTable } });
            }
        }
        catch (Exception ex)
        {
            context.Log($"Excel okuma hatası: {ex.Message}", LogLevel.Error);
            throw;
        }
    }

    public ActivityMetadata GetMetadata()
    {
        return new ActivityMetadata
        {
            ActivityId = "Excel.Read",
            DisplayName = "Excel Oku",
            Category = "Excel",
            Description = "Aralık/tablo okur → DataTable.",
            Inputs = new List<ActivityParameter>
            {
                new() { Name = "filePath", Type = "string", Required = true, Description = "Excel dosyası yolu" },
                new() { Name = "sheet", Type = "string", Required = false, Description = "Sheet adı (varsayılan: ilk sheet)" },
                new() { Name = "range", Type = "string", Required = false, Description = "Okuma aralığı (örn: A1:C10)" }
            },
            Outputs = new List<ActivityParameter>
            {
                new() { Name = "data", Type = "DataTable", Required = true, Description = "Okunan veriler" }
            },
            RequiredCapabilities = new List<string> { "excel" }
        };
    }
}

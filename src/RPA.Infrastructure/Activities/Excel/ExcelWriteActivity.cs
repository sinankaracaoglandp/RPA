namespace RPA.Infrastructure.Activities.Excel;

using System.Data;
using ClosedXML.Excel;
using RPA.Domain.Interfaces;

/// <summary>
/// DataTable'ı Excel dosyasına yazar.
/// Aktivite ID: Excel.Write
/// </summary>
public class ExcelWriteActivity : IActivity
{
    public Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        // Giriş parametrelerini al
        string filePath = context.GetVariable<string>("filePath");
        string? sheet = context.GetVariable<string?>("sheet");
        DataTable? data = context.GetVariable<DataTable?>("data");
        string? startCell = context.GetVariable<string?>("startCell") ?? "A1";

        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("filePath boş olamaz.");

        if (data == null || data.Rows.Count == 0)
            throw new ArgumentException("data null veya boş olamaz.");

        try
        {
            IXLWorkbook workbook;

            // Dosya varsa aç, yoksa yeni oluştur
            if (File.Exists(filePath))
            {
                workbook = new XLWorkbook(filePath);
            }
            else
            {
                workbook = new XLWorkbook();
            }

            using (workbook)
            {
                // Sheet'i belirle veya oluştur
                IXLWorksheet worksheet;
                if (!string.IsNullOrWhiteSpace(sheet))
                {
                    if (!workbook.TryGetWorksheet(sheet, out worksheet!))
                    {
                        worksheet = workbook.Worksheets.Add(sheet);
                    }
                }
                else
                {
                    // Varsayılan: ilk sheet veya yeni oluştur
                    worksheet = workbook.Worksheets.Count > 0
                        ? workbook.Worksheet(1)
                        : workbook.Worksheets.Add("Sheet1");
                }

                // DataTable'ı sheet'e yaz
                var startAddress = worksheet.Cell(startCell).Address;
                int rowIndex = startAddress.RowNumber;
                int colIndex = startAddress.ColumnNumber;

                // Header yazma
                for (int i = 0; i < data.Columns.Count; i++)
                {
                    worksheet.Cell(rowIndex, colIndex + i).Value = data.Columns[i].ColumnName;
                }

                // Veri satırları yazma
                for (int r = 0; r < data.Rows.Count; r++)
                {
                    for (int c = 0; c < data.Columns.Count; c++)
                    {
                        var cellValue = data.Rows[r][c];
                        if (cellValue != null && cellValue != DBNull.Value)
                            worksheet.Cell(rowIndex + r + 1, colIndex + c).Value = (XLCellValue)cellValue;
                        else
                            worksheet.Cell(rowIndex + r + 1, colIndex + c).Value = "";
                    }
                }

                // Sütun genişliğini otomatik ayarla
                worksheet.Columns(colIndex, colIndex + data.Columns.Count - 1).AdjustToContents();

                // Dosyayı kaydet
                workbook.SaveAs(filePath);

                context.Log($"Excel dosyası yazıldı: {filePath}, Sheet: {worksheet.Name}, Satır: {data.Rows.Count}");
            }

            return Task.FromResult(new Dictionary<string, object?> { { "success", true } });
        }
        catch (Exception ex)
        {
            context.Log($"Excel yazma hatası: {ex.Message}", LogLevel.Error);
            throw;
        }
    }

    public ActivityMetadata GetMetadata()
    {
        return new ActivityMetadata
        {
            ActivityId = "Excel.Write",
            DisplayName = "Excel Yaz",
            Category = "Excel",
            Description = "DataTable'ı çalışma sayfasına yazar.",
            Inputs = new List<ActivityParameter>
            {
                new() { Name = "filePath", Type = "string", Required = true, Description = "Excel dosyası yolu" },
                new() { Name = "sheet", Type = "string", Required = false, Description = "Sheet adı (varsayılan: ilk sheet)" },
                new() { Name = "data", Type = "DataTable", Required = true, Description = "Yazılacak veriler" },
                new() { Name = "startCell", Type = "string", Required = false, DefaultValue = "A1", Description = "Başlangıç hücresi" }
            },
            Outputs = new List<ActivityParameter>
            {
                new() { Name = "success", Type = "bool", Required = true, Description = "Yazma başarılı mı" }
            },
            RequiredCapabilities = new List<string> { "excel" }
        };
    }
}

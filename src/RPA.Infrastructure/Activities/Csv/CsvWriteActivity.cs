namespace RPA.Infrastructure.Activities.Csv;

using System.Data;
using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using RPA.Domain.Interfaces;

/// <summary>
/// DataTable'ı CSV dosyasına yazar.
/// Aktivite ID: Csv.Write
/// </summary>
public class CsvWriteActivity : IActivity
{
    public Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        // Giriş parametrelerini al
        string filePath = context.GetVariable<string>("filePath");
        DataTable? data = context.GetVariable<DataTable?>("data");
        string? delimiter = context.GetVariable<string?>("delimiter") ?? ",";

        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("filePath boş olamaz.");

        if (data == null || data.Rows.Count == 0)
            throw new ArgumentException("data null veya boş olamaz.");

        try
        {
            using (var writer = new StreamWriter(filePath))
            {
                var config = new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = delimiter };
                using (var csv = new CsvWriter(writer, config))
                {
                    // Header satırını yaz
                    foreach (DataColumn column in data.Columns)
                    {
                        csv.WriteField(column.ColumnName);
                    }
                    csv.NextRecord();

                    // Veri satırlarını yaz
                    foreach (DataRow row in data.Rows)
                    {
                        for (int i = 0; i < data.Columns.Count; i++)
                        {
                            csv.WriteField(row[i]);
                        }
                        csv.NextRecord();
                    }
                }
            }

            context.Log($"CSV dosyası yazıldı: {filePath}, Satır: {data.Rows.Count}, Delimiter: '{delimiter}'");

            return Task.FromResult(new Dictionary<string, object?> { { "success", true } });
        }
        catch (Exception ex)
        {
            context.Log($"CSV yazma hatası: {ex.Message}", LogLevel.Error);
            throw;
        }
    }

    public ActivityMetadata GetMetadata()
    {
        return new ActivityMetadata
        {
            ActivityId = "Csv.Write",
            DisplayName = "CSV Yaz",
            Category = "CSV",
            Description = "DataTable'ı CSV'ye yazar.",
            Inputs = new List<ActivityParameter>
            {
                new() { Name = "filePath", Type = "string", Required = true, Description = "CSV dosyası yolu" },
                new() { Name = "data", Type = "DataTable", Required = true, Description = "Yazılacak veriler" },
                new() { Name = "delimiter", Type = "string", Required = false, DefaultValue = ",", Description = "Sütun ayırıcı karakteri" }
            },
            Outputs = new List<ActivityParameter>
            {
                new() { Name = "success", Type = "bool", Required = true, Description = "Yazma başarılı mı" }
            },
            RequiredCapabilities = new List<string> { "csv" }
        };
    }
}

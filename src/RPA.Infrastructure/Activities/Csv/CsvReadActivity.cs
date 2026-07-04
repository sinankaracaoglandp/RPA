namespace RPA.Infrastructure.Activities.Csv;

using System.Data;
using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using RPA.Domain.Interfaces;

/// <summary>
/// CSV dosyasını DataTable olarak okur.
/// Aktivite ID: Csv.Read
/// </summary>
public class CsvReadActivity : IActivity
{
    public Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        // Giriş parametrelerini al
        string filePath = context.GetVariable<string>("filePath");
        string? delimiter = context.GetVariable<string?>("delimiter") ?? ",";

        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("filePath boş olamaz.");

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"CSV dosyası bulunamadı: {filePath}");

        try
        {
            var dataTable = new DataTable();

            using (var reader = new StreamReader(filePath))
            {
                var config = new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = delimiter };
                using (var csv = new CsvReader(reader, config))
                {

                    // Header satırını oku
                    csv.Read();
                    csv.ReadHeader();

                    if (csv.HeaderRecord == null || csv.HeaderRecord.Length == 0)
                        throw new InvalidOperationException("CSV dosyasında header satırı bulunamadı.");

                    // Kolon adlarını DataTable'a ekle
                    foreach (var columnName in csv.HeaderRecord)
                    {
                        dataTable.Columns.Add(columnName ?? $"Column{dataTable.Columns.Count}");
                    }

                    // Veri satırlarını oku
                    while (csv.Read())
                    {
                        var values = new object?[dataTable.Columns.Count];
                        for (int i = 0; i < dataTable.Columns.Count; i++)
                        {
                            values[i] = csv.GetField(i);
                        }
                        dataTable.Rows.Add(values);
                    }
                }
            }

            context.Log($"CSV dosyası okundu: {filePath}, Satır: {dataTable.Rows.Count}, Delimiter: '{delimiter}'");

            return Task.FromResult(new Dictionary<string, object?> { { "data", dataTable } });
        }
        catch (Exception ex)
        {
            context.Log($"CSV okuma hatası: {ex.Message}", LogLevel.Error);
            throw;
        }
    }

    public ActivityMetadata GetMetadata()
    {
        return new ActivityMetadata
        {
            ActivityId = "Csv.Read",
            DisplayName = "CSV Oku",
            Category = "CSV",
            Description = "CSV dosyasını DataTable olarak okur.",
            Inputs = new List<ActivityParameter>
            {
                new() { Name = "filePath", Type = "string", Required = true, Description = "CSV dosyası yolu" },
                new() { Name = "delimiter", Type = "string", Required = false, DefaultValue = ",", Description = "Sütun ayırıcı karakteri" }
            },
            Outputs = new List<ActivityParameter>
            {
                new() { Name = "data", Type = "DataTable", Required = true, Description = "Okunan veriler" }
            },
            RequiredCapabilities = new List<string> { "csv" }
        };
    }
}

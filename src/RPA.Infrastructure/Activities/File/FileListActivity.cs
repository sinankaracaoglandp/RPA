namespace RPA.Infrastructure.Activities.File;

using RPA.Domain.Interfaces;
using Newtonsoft.Json.Linq;

public sealed class FileListActivity : IActivity
{
    public ActivityMetadata GetMetadata() => new()
    {
        ActivityId = "File.List",
        DisplayName = "Dosya Listele",
        Category = "Dosya",
        Description = "Klasördeki dosyaları listeler (pattern).",
        Inputs = new()
        {
            new ActivityParameter { Name = "folder", Type = "string", Required = true, Description = "Klasör yolu", PickerKind = "folder" },
            new ActivityParameter { Name = "pattern", Type = "string", Required = false, DefaultValue = "*", Description = "Dosya adı deseni (birden çok uzantı için ; veya , ile ayır)" },
            new ActivityParameter { Name = "outputVariable", Type = "string", Required = false, DefaultValue = "dosyalar", Description = "Dosya listesinin atanacağı değişken" }
        },
        Outputs = new() { new ActivityParameter { Name = "files", Type = "JSON", Required = false, Description = "Dosya listesi" } },
        RequiredCapabilities = new() { "file" }
    };

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var folder = context.GetVariable<string>("folder");
        var pattern = context.GetVariable<string>("pattern") ?? "*";

        if (string.IsNullOrWhiteSpace(folder))
            throw new Domain.Exceptions.BusinessException("'folder' parametresi boş olamaz.");

        if (!System.IO.Directory.Exists(folder))
            throw new Domain.Exceptions.BusinessException($"Klasör bulunamadı: {folder}");

        try
        {
            context.Log($"Klasör taranıyor: {folder}, desen: {pattern}");
            var patterns = ParsePatterns(pattern);
            // Birden çok desen aynı dosyayı döndürebilir (örn. *.* + *.pdf); yolları benzersizleştir.
            var files = patterns
                .SelectMany(p => System.IO.Directory.GetFiles(folder, p, System.IO.SearchOption.TopDirectoryOnly))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var fileList = new JArray();

            foreach (var filePath in files)
            {
                var fileInfo = new System.IO.FileInfo(filePath);
                fileList.Add(new JObject
                {
                    ["name"] = fileInfo.Name,
                    ["path"] = filePath,
                    ["size"] = fileInfo.Length,
                    ["createdAt"] = fileInfo.CreationTime,
                    ["modifiedAt"] = fileInfo.LastWriteTime
                });
            }

            context.Log($"Dosya listesi hazırlandı: {files.Length} dosya bulundu");

            var outputs = new Dictionary<string, object?> { ["files"] = fileList };

            // Kullanıcı bir çıktı değişkeni seçtiyse dosya listesini ona da bağla (Web.GetText deseni)
            // → sonraki node'lar (ör. Logic.ForEach) {{degisken}} ile listeye erişebilir.
            var outputVariable = context.GetVariable<string>("outputVariable")?.Trim();
            if (!string.IsNullOrWhiteSpace(outputVariable))
            {
                context.SetVariable(outputVariable, fileList);
                outputs[outputVariable] = fileList;
            }

            return outputs;
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new Domain.Exceptions.BusinessException($"Klasör okuma izni reddedildi: {ex.Message}");
        }
        catch (System.IO.IOException ex)
        {
            throw new Domain.Exceptions.SystemException($"Klasör tarama sırasında hata: {ex.Message}");
        }
    }

    /// <summary>
    /// Deseni birden çok uzantı filtresine ayırır. Ayraçlar: <c>;</c> ve <c>,</c>
    /// (örn. <c>*.pdf;*.xlsx</c>). Boş/whitespace giriş varsayılan <c>*</c> döner.
    /// </summary>
    internal static IReadOnlyList<string> ParsePatterns(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return new[] { "*" };
        }

        var parts = pattern
            .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return parts.Length == 0 ? new[] { "*" } : parts;
    }
}

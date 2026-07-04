namespace RPA.Infrastructure.Activities.File;

using System.IO.Compression;
using RPA.Domain.Interfaces;
using Newtonsoft.Json.Linq;

/// <summary>
/// Zip arşivini hedef klasöre açar (Spec 5.3).
/// </summary>
public sealed class FileUnzipActivity : IActivity
{
    public ActivityMetadata GetMetadata()
    {
        return new ActivityMetadata
        {
            ActivityId = "File.Unzip",
            DisplayName = "Aç (Unzip)",
            Category = "Dosya",
            Description = "Zip arşivini hedef klasöre açar.",
            Inputs = new()
            {
                new ActivityParameter { Name = "zipPath", Type = "string", Required = true, Description = "Zip dosyasının yolu" },
                new ActivityParameter { Name = "targetFolder", Type = "string", Required = true, Description = "Hedef klasör yolu" }
            },
            Outputs = new()
            {
                new ActivityParameter { Name = "files", Type = "JSON", Required = false, Description = "Açılan dosya listesi" }
            },
            RequiredCapabilities = new() { "file" }
        };
    }

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var zipPath = context.GetVariable<string>("zipPath");
        var targetFolder = context.GetVariable<string>("targetFolder");

        if (string.IsNullOrWhiteSpace(zipPath))
        {
            throw new Domain.Exceptions.BusinessException("'zipPath' parametresi boş olamaz.");
        }

        if (string.IsNullOrWhiteSpace(targetFolder))
        {
            throw new Domain.Exceptions.BusinessException("'targetFolder' parametresi boş olamaz.");
        }

        if (!File.Exists(zipPath))
        {
            throw new Domain.Exceptions.BusinessException($"Zip dosyası bulunamadı: {zipPath}");
        }

        try
        {
            context.Log($"Açılıyor: {zipPath} → {targetFolder}");

            // Ensure target directory exists
            if (!Directory.Exists(targetFolder))
            {
                Directory.CreateDirectory(targetFolder);
            }

            var extractedFiles = new JArray();

            using (var zipFile = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in zipFile.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        // Skip directories
                        continue;
                    }

                    var fullPath = Path.Combine(targetFolder, entry.FullName);
                    var directory = Path.GetDirectoryName(fullPath);

                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    entry.ExtractToFile(fullPath, overwrite: true);
                    extractedFiles.Add(fullPath);
                }
            }

            context.Log($"Açılma tamamlandı: {extractedFiles.Count} dosya çıkartıldı");
            return new Dictionary<string, object?> { ["files"] = extractedFiles };
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new Domain.Exceptions.BusinessException($"Açılma izni reddedildi: {ex.Message}");
        }
        catch (IOException ex)
        {
            throw new Domain.Exceptions.SystemException($"Açılma sırasında hata: {ex.Message}");
        }
    }
}

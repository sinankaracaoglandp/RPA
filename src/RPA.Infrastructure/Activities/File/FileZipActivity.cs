namespace RPA.Infrastructure.Activities.File;

using System.IO.Compression;
using RPA.Domain.Interfaces;

/// <summary>
/// Dosya/klasörü zip arşivine ekler (Spec 5.3).
/// </summary>
public sealed class FileZipActivity : IActivity
{
    public ActivityMetadata GetMetadata()
    {
        return new ActivityMetadata
        {
            ActivityId = "File.Zip",
            DisplayName = "Sıkıştır (Zip)",
            Category = "Dosya",
            Description = "Dosya/klasörü zip arşivine ekler.",
            Inputs = new()
            {
                new ActivityParameter { Name = "source", Type = "string", Required = true, Description = "Kaynak dosya/klasör yolu" },
                new ActivityParameter { Name = "zipPath", Type = "string", Required = true, Description = "Hedef zip dosyası yolu" }
            },
            Outputs = new()
            {
                new ActivityParameter { Name = "path", Type = "string", Required = false, Description = "Oluşturulan zip dosyasının yolu" }
            },
            RequiredCapabilities = new() { "file" }
        };
    }

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var source = context.GetVariable<string>("source");
        var zipPath = context.GetVariable<string>("zipPath");

        if (string.IsNullOrWhiteSpace(source))
        {
            throw new Domain.Exceptions.BusinessException("'source' parametresi boş olamaz.");
        }

        if (string.IsNullOrWhiteSpace(zipPath))
        {
            throw new Domain.Exceptions.BusinessException("'zipPath' parametresi boş olamaz.");
        }

        bool isDirectory = Directory.Exists(source);
        bool isFile = File.Exists(source);

        if (!isDirectory && !isFile)
        {
            throw new Domain.Exceptions.BusinessException($"Kaynak dosya/klasör bulunamadı: {source}");
        }

        try
        {
            context.Log($"Sıkıştırma başlanıyor: {source} → {zipPath}");

            // Ensure the destination directory exists
            var zipDir = Path.GetDirectoryName(zipPath);
            if (!string.IsNullOrWhiteSpace(zipDir) && !Directory.Exists(zipDir))
            {
                Directory.CreateDirectory(zipDir);
            }

            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            if (isFile)
            {
                // Zip single file
                using (var zipFile = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                {
                    zipFile.CreateEntryFromFile(source, Path.GetFileName(source));
                }
            }
            else
            {
                // Zip directory
                ZipFile.CreateFromDirectory(source, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            }

            context.Log($"Sıkıştırma tamamlandı: {zipPath}");
            return new Dictionary<string, object?> { ["path"] = zipPath };
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new Domain.Exceptions.BusinessException($"Sıkıştırma izni reddedildi: {ex.Message}");
        }
        catch (IOException ex)
        {
            throw new Domain.Exceptions.SystemException($"Sıkıştırma sırasında hata: {ex.Message}");
        }
    }
}

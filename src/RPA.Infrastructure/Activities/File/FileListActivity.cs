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
            new ActivityParameter { Name = "folder", Type = "string", Required = true, Description = "Klasör yolu" },
            new ActivityParameter { Name = "pattern", Type = "string", Required = false, DefaultValue = "*", Description = "Dosya adı deseni" }
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
            var files = System.IO.Directory.GetFiles(folder, pattern, System.IO.SearchOption.TopDirectoryOnly);
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
            return new Dictionary<string, object?> { ["files"] = fileList };
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
}

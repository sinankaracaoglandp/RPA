namespace RPA.Infrastructure.Activities.File;

using RPA.Domain.Interfaces;

/// <summary>
/// Dosyayı siler (Spec 5.3).
/// </summary>
public sealed class FileDeleteActivity : IActivity
{
    public ActivityMetadata GetMetadata()
    {
        return new ActivityMetadata
        {
            ActivityId = "File.Delete",
            DisplayName = "Dosya Sil",
            Category = "Dosya",
            Description = "Dosyayı siler.",
            Inputs = new()
            {
                new ActivityParameter { Name = "path", Type = "string", Required = true, Description = "Silinecek dosya yolu" }
            },
            Outputs = new(),
            RequiredCapabilities = new() { "file" }
        };
    }

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var path = context.GetVariable<string>("path");

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new Domain.Exceptions.BusinessException("'path' parametresi boş olamaz.");
        }

        if (!File.Exists(path))
        {
            throw new Domain.Exceptions.BusinessException($"Dosya bulunamadı: {path}");
        }

        try
        {
            context.Log($"Dosya siliniyor: {path}");
            File.Delete(path);
            context.Log($"Dosya başarıyla silindi: {path}");
            return new();
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new Domain.Exceptions.BusinessException($"Dosya silme izni reddedildi: {ex.Message}");
        }
        catch (IOException ex)
        {
            throw new Domain.Exceptions.SystemException($"Dosya silme sırasında hata: {ex.Message}");
        }
    }
}

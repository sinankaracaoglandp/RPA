namespace RPA.Infrastructure.Activities.File;

using RPA.Domain.Interfaces;

/// <summary>
/// Dosyayı hedefe kopyalar (Spec 5.3).
/// </summary>
public sealed class FileCopyActivity : IActivity
{
    public ActivityMetadata GetMetadata()
    {
        return new ActivityMetadata
        {
            ActivityId = "File.Copy",
            DisplayName = "Dosya Kopyala",
            Category = "Dosya",
            Description = "Dosyayı hedefe kopyalar.",
            Inputs = new()
            {
                new ActivityParameter { Name = "source", Type = "string", Required = true, Description = "Kaynak dosya yolu" },
                new ActivityParameter { Name = "destination", Type = "string", Required = true, Description = "Hedef dosya yolu" },
                new ActivityParameter { Name = "overwrite", Type = "bool", Required = false, DefaultValue = false, Description = "Hedefteki dosyayı üzerine yaz" }
            },
            Outputs = new(),
            RequiredCapabilities = new() { "file" }
        };
    }

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var source = context.GetVariable<string>("source");
        var destination = context.GetVariable<string>("destination");
        var overwrite = context.GetVariable<bool?>("overwrite") ?? false;

        if (string.IsNullOrWhiteSpace(source))
        {
            throw new Domain.Exceptions.BusinessException("'source' parametresi boş olamaz.");
        }

        if (string.IsNullOrWhiteSpace(destination))
        {
            throw new Domain.Exceptions.BusinessException("'destination' parametresi boş olamaz.");
        }

        if (!System.IO.File.Exists(source))
        {
            throw new Domain.Exceptions.BusinessException($"Kaynak dosya bulunamadı: {source}");
        }

        try
        {
            context.Log($"Dosya kopyalanıyor: {source} → {destination}");
            System.IO.File.Copy(source, destination, overwrite);
            context.Log($"Dosya başarıyla kopyalandı: {destination}");
            return new();
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new Domain.Exceptions.BusinessException($"Dosya kopyalama izni reddedildi: {ex.Message}");
        }
        catch (System.IO.IOException ex)
        {
            throw new Domain.Exceptions.SystemException($"Dosya kopyalama sırasında hata: {ex.Message}");
        }
    }
}

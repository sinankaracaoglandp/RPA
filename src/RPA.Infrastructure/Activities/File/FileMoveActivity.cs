namespace RPA.Infrastructure.Activities.File;

using RPA.Domain.Interfaces;

/// <summary>
/// Dosyayı taşır/yeniden adlandırır (Spec 5.3).
/// </summary>
public sealed class FileMoveActivity : IActivity
{
    public ActivityMetadata GetMetadata()
    {
        return new ActivityMetadata
        {
            ActivityId = "File.Move",
            DisplayName = "Dosya Taşı",
            Category = "Dosya",
            Description = "Dosyayı taşır/yeniden adlandırır.",
            Inputs = new()
            {
                new ActivityParameter { Name = "source", Type = "string", Required = true, Description = "Kaynak dosya yolu" },
                new ActivityParameter { Name = "destination", Type = "string", Required = true, Description = "Hedef dosya yolu" }
            },
            Outputs = new(),
            RequiredCapabilities = new() { "file" }
        };
    }

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var source = context.GetVariable<string>("source");
        var destination = context.GetVariable<string>("destination");

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
            context.Log($"Dosya taşınıyor: {source} → {destination}");
            System.IO.File.Move(source, destination, overwrite: true);
            context.Log($"Dosya başarıyla taşındı: {destination}");
            return new();
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new Domain.Exceptions.BusinessException($"Dosya taşıma izni reddedildi: {ex.Message}");
        }
        catch (System.IO.IOException ex)
        {
            throw new Domain.Exceptions.SystemException($"Dosya taşıma sırasında hata: {ex.Message}");
        }
    }
}

namespace RPA.Infrastructure.Workflow;

/// <summary>
/// Workflow JSON şema doğrulama sonucu.
/// Studio "Yayınla" öncesi doğrulamada ve BaseRunner yükleme anında kullanılır.
/// </summary>
public sealed class WorkflowValidationResult
{
    /// <summary>Doğrulama geçti mi?</summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>İnsan okunabilir (Türkçe) hata mesajları — property yolu dahil.</summary>
    public IReadOnlyList<string> Errors { get; }

    private WorkflowValidationResult(IReadOnlyList<string> errors) => Errors = errors;

    public static WorkflowValidationResult Success() => new(Array.Empty<string>());

    public static WorkflowValidationResult Failure(IReadOnlyList<string> errors) =>
        new(errors is { Count: > 0 } ? errors : new[] { "Bilinmeyen doğrulama hatası." });
}

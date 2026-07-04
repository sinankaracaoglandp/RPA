namespace RPA.Domain.Exceptions;

using RPA.Domain.Enums;

/// <summary>
/// İş kuralı ihlali (beklenen hata). Örn. "Malzeme zaten mevcut", "Geçersiz SAP dönüşü".
/// BaseRunner tarafından yakalanır ve Action Center'a düşürülür (Faz 6).
/// Retry uygulanmaz — insan müdahalesi gerektirir.
/// Spec Bölüm 5.2, 6.
/// </summary>
public class BusinessException : Exception
{
    /// <summary>İstisna sınıflandırması — daima <see cref="ExceptionType.Business"/>.</summary>
    public ExceptionType ExceptionType => ExceptionType.Business;

    public BusinessException(string message)
        : base(message)
    {
    }

    public BusinessException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

namespace RPA.Domain.Exceptions;

using RPA.Domain.Enums;

/// <summary>
/// Teknik hata (beklenmeyen). Örn. "Bağlantı timeout", "RFC_COMMUNICATION_FAILURE".
/// BaseRunner tarafından yakalanır ve retry politikası uygulanır (Task 2.3).
/// Spec Bölüm 5.2, 6.
///
/// Not: <c>System.SystemException</c> ile karışmaması için tüketen dosyalarda
/// <c>using SystemException = RPA.Domain.Exceptions.SystemException;</c> alias'ı kullanılır.
/// </summary>
public class SystemException : Exception
{
    /// <summary>İstisna sınıflandırması — daima <see cref="ExceptionType.System"/>.</summary>
    public ExceptionType ExceptionType => ExceptionType.System;

    public SystemException(string message)
        : base(message)
    {
    }

    public SystemException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

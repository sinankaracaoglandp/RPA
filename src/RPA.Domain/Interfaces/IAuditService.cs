namespace RPA.Domain.Interfaces;

/// <summary>
/// Kullanıcı aksiyonlarını merkezi olarak loglayan servis (Spec Bölüm 11).
/// Entity değişikliklerinin yanı sıra, entity değişikliği tetiklemeyen
/// aksiyonlar (login, run, approve vb.) için de kullanılır.
/// </summary>
public interface IAuditService
{
    Task LogAsync(
        Guid userId,
        string action,
        string resourceType,
        Guid resourceId,
        string? oldValue = null,
        string? newValue = null,
        CancellationToken cancellationToken = default);
}

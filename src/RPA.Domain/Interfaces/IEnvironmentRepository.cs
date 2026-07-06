namespace RPA.Domain.Interfaces;

using RPA.Domain.Entities;
using Environment = RPA.Domain.Entities.Environment;

/// <summary>
/// Ortam (Dev/Test/Prod) kalıcılık soyutlaması (WP-6.4). Ortam yönetimi ekranı ve
/// workflow deployment governance akışının hedef ortamlarını sağlar.
/// </summary>
public interface IEnvironmentRepository
{
    /// <summary>Silinmemiş tüm ortamlar (ada göre sıralı).</summary>
    Task<IReadOnlyList<Environment>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Ada göre ortam bulur (case-insensitive); yoksa null.</summary>
    Task<Environment?> FindByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Yeni ortam ekler.</summary>
    Task<Environment> AddAsync(Environment environment, CancellationToken cancellationToken = default);

    /// <summary>Değişiklikleri kalıcı hale getirir.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

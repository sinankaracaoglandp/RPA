namespace RPA.Domain.Interfaces;

using RPA.Domain.Entities;

/// <summary>
/// Action Center kayıt kalıcılık soyutlaması (WP-6.2). Bekleyen kayıtların listelenmesi ve
/// tekil güncelleme (atama/çözümleme) işlemlerini destekler.
/// </summary>
public interface IActionItemRepository
{
    /// <summary>Bekleyen (Pending) kayıtlar; type verilirse filtreli (en yeni önce).</summary>
    Task<IReadOnlyList<ActionItem>> ListPendingAsync(string? type, CancellationToken cancellationToken = default);

    /// <summary>Kaydı Id ile döner; yoksa null.</summary>
    Task<ActionItem?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Değişiklikleri kalıcı hale getirir.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

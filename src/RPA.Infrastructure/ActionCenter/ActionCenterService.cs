namespace RPA.Infrastructure.ActionCenter;

using RPA.Domain.Entities;
using RPA.Domain.Interfaces;

/// <summary>
/// Action Center servisi (WP-6.2, Spec Bölüm 8.2, 6): bekleyen kayıtların (BusinessException/
/// OTP/Onay) listelenmesi, operatöre atanması ve çözümlenmesi (durum + not). Read/mutate;
/// bildirim ve OTP kanal orkestrasyonu kapsam dışı (WP-4.3 / WP-6.3).
/// </summary>
public sealed class ActionCenterService
{
    private readonly IActionItemRepository _repository;

    public ActionCenterService(IActionItemRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <summary>Bekleyen (Pending) kayıtları döner; type verilirse ona göre filtreler (en yeni önce).</summary>
    public Task<IReadOnlyList<ActionItem>> ListPendingAsync(
        string? type, CancellationToken cancellationToken = default)
        => _repository.ListPendingAsync(type, cancellationToken);

    /// <summary>Kaydı Id ile döner; yoksa null.</summary>
    public Task<ActionItem?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => _repository.FindByIdAsync(id, cancellationToken);

    /// <summary>Kaydı bir kullanıcıya atar. Bulunamazsa null.</summary>
    public async Task<ActionItem?> AssignAsync(
        Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var item = await _repository.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return null;
        }

        item.AssignedUserId = userId;
        await _repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return item;
    }

    /// <summary>Kaydı çözümler: Status = Resolved, çözüm notu ve zaman damgası. Bulunamazsa null.</summary>
    public async Task<ActionItem?> ResolveAsync(
        Guid id, string? note, CancellationToken cancellationToken = default)
    {
        var item = await _repository.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return null;
        }

        item.Status = "Resolved";
        item.ResolutionNote = note;
        item.ResolvedAt = DateTime.UtcNow;
        await _repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return item;
    }
}

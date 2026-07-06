namespace RPA.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using RPA.Domain.Entities;
using RPA.Domain.Interfaces;

/// <summary>EF Core tabanlı <see cref="IActionItemRepository"/> implementasyonu (WP-6.2).</summary>
public sealed class EfActionItemRepository : IActionItemRepository
{
    private readonly RpaDbContext _db;

    public EfActionItemRepository(RpaDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<IReadOnlyList<ActionItem>> ListPendingAsync(
        string? type, CancellationToken cancellationToken = default)
    {
        var q = _db.ActionItems.AsNoTracking()
            .Where(a => !a.IsDeleted && a.Status == "Pending");

        if (!string.IsNullOrWhiteSpace(type))
        {
            q = q.Where(a => a.Type == type);
        }

        return await q
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<ActionItem?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _db.ActionItems.FirstOrDefaultAsync(a => a.Id == id && !a.IsDeleted, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _db.SaveChangesAsync(cancellationToken);
}

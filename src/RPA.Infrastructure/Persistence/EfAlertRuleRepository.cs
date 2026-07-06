namespace RPA.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using RPA.Domain.Entities;
using RPA.Domain.Interfaces;

/// <summary>EF Core tabanlı <see cref="IAlertRuleRepository"/> implementasyonu (WP-6.3).</summary>
public sealed class EfAlertRuleRepository : IAlertRuleRepository
{
    private readonly RpaDbContext _db;

    public EfAlertRuleRepository(RpaDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<IReadOnlyList<AlertRule>> ListActiveAsync(CancellationToken cancellationToken = default)
        => await _db.AlertRules.AsNoTracking()
            .Where(a => !a.IsDeleted && a.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<AlertRule>> ListAllAsync(CancellationToken cancellationToken = default)
        => await _db.AlertRules.AsNoTracking()
            .Where(a => !a.IsDeleted)
            .OrderBy(a => a.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task<AlertRule?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _db.AlertRules.FirstOrDefaultAsync(a => a.Id == id && !a.IsDeleted, cancellationToken);

    public async Task AddAsync(AlertRule rule, CancellationToken cancellationToken = default)
        => await _db.AlertRules.AddAsync(rule, cancellationToken).ConfigureAwait(false);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _db.SaveChangesAsync(cancellationToken);
}

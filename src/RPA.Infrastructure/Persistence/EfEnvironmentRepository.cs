namespace RPA.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using RPA.Domain.Entities;
using RPA.Domain.Interfaces;
using Environment = RPA.Domain.Entities.Environment;

/// <summary>EF Core tabanlı <see cref="IEnvironmentRepository"/> implementasyonu (WP-6.4).</summary>
public sealed class EfEnvironmentRepository : IEnvironmentRepository
{
    private readonly RpaDbContext _db;

    public EfEnvironmentRepository(RpaDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<IReadOnlyList<Environment>> ListAsync(CancellationToken cancellationToken = default)
        => await _db.Environments.AsNoTracking()
            .Where(e => !e.IsDeleted)
            .OrderBy(e => e.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task<Environment?> FindByNameAsync(string name, CancellationToken cancellationToken = default)
        => _db.Environments
            .FirstOrDefaultAsync(e => !e.IsDeleted && e.Name.ToLower() == name.ToLower(), cancellationToken);

    public async Task<Environment> AddAsync(Environment environment, CancellationToken cancellationToken = default)
    {
        await _db.Environments.AddAsync(environment, cancellationToken).ConfigureAwait(false);
        return environment;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _db.SaveChangesAsync(cancellationToken);
}

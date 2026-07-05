namespace RPA.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;

/// <summary>EF Core tabanlı <see cref="IRobotRepository"/> implementasyonu (Task 3.1).</summary>
public sealed class EfRobotRepository : IRobotRepository
{
    private readonly RpaDbContext _db;

    public EfRobotRepository(RpaDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public Task<Robot?> FindByMachineNameAsync(string machineName, CancellationToken cancellationToken = default)
        => _db.Robots
            .Where(r => !r.IsDeleted && r.MachineName == machineName)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<Robot?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _db.Robots.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted, cancellationToken);

    public async Task<IReadOnlyList<Robot>> FindStaleAsync(DateTime olderThanUtc, CancellationToken cancellationToken = default)
        => await _db.Robots
            .Where(r => !r.IsDeleted
                        && r.Status != RobotStatus.Offline
                        && r.LastHeartbeat != null
                        && r.LastHeartbeat < olderThanUtc)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Robot robot, CancellationToken cancellationToken = default)
        => await _db.Robots.AddAsync(robot, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _db.SaveChangesAsync(cancellationToken);
}

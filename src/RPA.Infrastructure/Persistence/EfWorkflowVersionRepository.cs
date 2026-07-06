namespace RPA.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using RPA.Domain.Entities;
using RPA.Domain.Interfaces;

/// <summary>EF Core tabanlı <see cref="IWorkflowVersionRepository"/> implementasyonu (WP-6.4).</summary>
public sealed class EfWorkflowVersionRepository : IWorkflowVersionRepository
{
    private readonly RpaDbContext _db;

    public EfWorkflowVersionRepository(RpaDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<IReadOnlyList<WorkflowVersion>> ListByWorkflowAsync(
        Guid workflowId, CancellationToken cancellationToken = default)
        => await _db.WorkflowVersions.AsNoTracking()
            .Where(v => !v.IsDeleted && v.WorkflowId == workflowId)
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task<WorkflowVersion?> FindAsync(
        Guid workflowId, string version, CancellationToken cancellationToken = default)
        => _db.WorkflowVersions
            .FirstOrDefaultAsync(
                v => !v.IsDeleted && v.WorkflowId == workflowId && v.Version == version,
                cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _db.SaveChangesAsync(cancellationToken);
}

namespace RPA.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using RPA.Domain.Entities;
using RPA.Domain.Interfaces;

public sealed class EfProjectRepository : IProjectRepository
{
    private readonly RpaDbContext _db;

    public EfProjectRepository(RpaDbContext db) => _db = db;

    public async Task<IReadOnlyList<Project>> ListAsync(CancellationToken ct = default)
        => await _db.Projects.Where(p => !p.IsDeleted).OrderBy(p => p.Name).ToListAsync(ct);

    public Task<Project?> FindAsync(Guid id, CancellationToken ct = default)
        => _db.Projects.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, ct);

    public async Task<Project> AddAsync(Project project, CancellationToken ct = default)
    {
        _db.Projects.Add(project);
        return await Task.FromResult(project);
    }

    public Task<int> CountWorkflowsAsync(Guid projectId, CancellationToken ct = default)
        => _db.Workflows.CountAsync(w => w.ProjectId == projectId && !w.IsDeleted, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}

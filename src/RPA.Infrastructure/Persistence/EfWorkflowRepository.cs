namespace RPA.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;

public sealed class EfWorkflowRepository : IWorkflowRepository
{
    private readonly RpaDbContext _db;

    public EfWorkflowRepository(RpaDbContext db) => _db = db;

    public async Task<IReadOnlyList<Workflow>> ListByProjectAsync(Guid projectId, CancellationToken ct = default)
        => await _db.Workflows
            .Where(w => w.ProjectId == projectId && !w.IsDeleted)
            .OrderByDescending(w => w.UpdatedAt)
            .ToListAsync(ct);

    public Task<Workflow?> FindAsync(Guid id, CancellationToken ct = default)
        => _db.Workflows.FirstOrDefaultAsync(w => w.Id == id && !w.IsDeleted, ct);

    public async Task<Workflow> AddAsync(Workflow workflow, CancellationToken ct = default)
    {
        _db.Workflows.Add(workflow);
        return await Task.FromResult(workflow);
    }

    public Task<WorkflowVersion?> FindDraftAsync(Guid workflowId, CancellationToken ct = default)
        => _db.WorkflowVersions.FirstOrDefaultAsync(
            v => v.WorkflowId == workflowId && v.Status == ComponentStatus.Draft && !v.IsDeleted, ct);

    public Task AddVersionAsync(WorkflowVersion version, CancellationToken ct = default)
    {
        _db.WorkflowVersions.Add(version);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}

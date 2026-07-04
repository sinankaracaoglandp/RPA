using RPA.Domain.Entities;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Persistence;

namespace RPA.Infrastructure.Audit;

/// <summary>
/// IAuditService implementasyonu — entity değişikliği tetiklemeyen aksiyonlar
/// (login, run, approve vb.) için doğrudan AuditLog kaydı yazar.
/// Entity CRUD aksiyonları AuditInterceptor tarafından otomatik yakalanır.
/// </summary>
public class AuditService : IAuditService
{
    private readonly RpaDbContext _dbContext;

    public AuditService(RpaDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task LogAsync(
        Guid userId,
        string action,
        string resourceType,
        Guid resourceId,
        string? oldValue = null,
        string? newValue = null,
        CancellationToken cancellationToken = default)
    {
        var auditLog = new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            OldValue = oldValue,
            NewValue = newValue,
            CreatedAt = DateTime.UtcNow,
        };

        _dbContext.AuditLogs.Add(auditLog);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}

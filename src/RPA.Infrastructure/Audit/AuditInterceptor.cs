using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RPA.Domain.Entities;
using RPA.Infrastructure.Persistence;

namespace RPA.Infrastructure.Audit;

/// <summary>
/// EF Core SaveChanges interceptor'ı — Added/Modified/Deleted entity'leri
/// yakalayıp AuditLog kayıtları üretir (Spec Bölüm 11).
/// AuditLog.IsFeatureEnabled = false ise (appsettings "AuditLog:Enabled") devre dışı bırakılabilir.
/// </summary>
public class AuditInterceptor : SaveChangesInterceptor
{
    private readonly bool _enabled;

    public AuditInterceptor(bool enabled = true)
    {
        _enabled = enabled;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        CaptureAuditEntries(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        CaptureAuditEntries(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void CaptureAuditEntries(DbContext? context)
    {
        if (!_enabled || context is null)
        {
            return;
        }

        var currentUserId = (context as RpaDbContext)?.CurrentUserId ?? Guid.Empty;

        var entries = context.ChangeTracker.Entries()
            .Where(e => e.Entity is not AuditLog
                        && (e.State == EntityState.Added
                            || e.State == EntityState.Modified
                            || e.State == EntityState.Deleted))
            .ToList();

        foreach (var entry in entries)
        {
            var auditLog = BuildAuditLog(entry, currentUserId);
            context.Set<AuditLog>().Add(auditLog);
        }
    }

    private static AuditLog BuildAuditLog(EntityEntry entry, Guid currentUserId)
    {
        var action = entry.State switch
        {
            EntityState.Added => "Created",
            EntityState.Modified => "Updated",
            EntityState.Deleted => "Deleted",
            _ => "Unknown",
        };

        var resourceId = entry.Entity is BaseEntity baseEntity ? baseEntity.Id : Guid.Empty;

        string? oldValue = null;
        string? newValue = null;

        if (entry.State is EntityState.Modified or EntityState.Deleted)
        {
            oldValue = SerializeValues(entry.OriginalValues);
        }

        if (entry.State is EntityState.Added or EntityState.Modified)
        {
            newValue = SerializeValues(entry.CurrentValues);
        }

        return new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = currentUserId,
            Action = action,
            ResourceType = entry.Entity.GetType().Name,
            ResourceId = resourceId,
            OldValue = oldValue,
            NewValue = newValue,
            CreatedAt = DateTime.UtcNow,
        };
    }

    private static string SerializeValues(PropertyValues values)
    {
        var dict = values.Properties.ToDictionary(
            p => p.Name,
            p => values[p]);

        return JsonSerializer.Serialize(dict);
    }
}

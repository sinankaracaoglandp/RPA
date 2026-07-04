using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RPA.Domain.Entities;
using RPA.Infrastructure.Audit;
using RPA.Infrastructure.Persistence;

namespace RPA.Infrastructure.Tests;

public class AuditTests
{
    private static RpaDbContext CreateContext(bool auditEnabled = true)
    {
        var options = new DbContextOptionsBuilder<RpaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new AuditInterceptor(auditEnabled))
            .Options;

        return new RpaDbContext(options);
    }

    [Fact]
    public async Task CreatingEntity_ProducesAuditLog_WithCreatedAction()
    {
        var currentUserId = Guid.NewGuid();
        await using var context = CreateContext();
        context.CurrentUserId = currentUserId;

        var user = new User
        {
            AdUsername = "jdoe",
            FullName = "John Doe",
            Email = "jdoe@example.com",
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        var auditLog = context.AuditLogs.Single(a => a.ResourceId == user.Id);

        Assert.Equal("Created", auditLog.Action);
        Assert.Equal(nameof(User), auditLog.ResourceType);
        Assert.Equal(currentUserId, auditLog.UserId);
        Assert.Null(auditLog.OldValue);
        Assert.NotNull(auditLog.NewValue);
        Assert.Contains("jdoe", auditLog.NewValue);
    }

    [Fact]
    public async Task ModifyingEntity_ProducesAuditLog_WithUpdatedActionAndOldNewValues()
    {
        var currentUserId = Guid.NewGuid();
        await using var context = CreateContext();
        context.CurrentUserId = currentUserId;

        var user = new User
        {
            AdUsername = "jdoe",
            FullName = "John Doe",
            Email = "jdoe@example.com",
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        user.FullName = "Jane Doe";
        await context.SaveChangesAsync();

        var updateLog = context.AuditLogs
            .Where(a => a.ResourceId == user.Id && a.Action == "Updated")
            .Single();

        Assert.Equal("Updated", updateLog.Action);
        Assert.NotNull(updateLog.OldValue);
        Assert.NotNull(updateLog.NewValue);
        Assert.Contains("John Doe", updateLog.OldValue);
        Assert.Contains("Jane Doe", updateLog.NewValue);
    }

    [Fact]
    public async Task DeletingEntity_ProducesAuditLog_WithDeletedAction()
    {
        await using var context = CreateContext();

        var user = new User
        {
            AdUsername = "jdoe",
            FullName = "John Doe",
            Email = "jdoe@example.com",
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        context.Users.Remove(user);
        await context.SaveChangesAsync();

        var deleteLog = context.AuditLogs
            .Where(a => a.ResourceId == user.Id && a.Action == "Deleted")
            .Single();

        Assert.Equal("Deleted", deleteLog.Action);
        Assert.NotNull(deleteLog.OldValue);
        Assert.Null(deleteLog.NewValue);
    }

    [Fact]
    public async Task WhenAuditDisabled_NoAuditLogsAreProduced()
    {
        await using var context = CreateContext(auditEnabled: false);

        var user = new User
        {
            AdUsername = "jdoe",
            FullName = "John Doe",
            Email = "jdoe@example.com",
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        Assert.Empty(context.AuditLogs);
    }

    [Fact]
    public async Task AuditService_LogAsync_WritesAuditLogDirectly()
    {
        await using var context = CreateContext(auditEnabled: false);
        var service = new AuditService(context);

        var userId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();

        await service.LogAsync(userId, "Run", "Workflow", resourceId, newValue: "{\"status\":\"started\"}");

        var log = context.AuditLogs.Single();
        Assert.Equal(userId, log.UserId);
        Assert.Equal("Run", log.Action);
        Assert.Equal("Workflow", log.ResourceType);
        Assert.Equal(resourceId, log.ResourceId);
        Assert.Equal("{\"status\":\"started\"}", log.NewValue);
    }
}

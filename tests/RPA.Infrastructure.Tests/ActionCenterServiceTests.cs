namespace RPA.Infrastructure.Tests;

using Microsoft.EntityFrameworkCore;
using RPA.Domain.Entities;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.ActionCenter;
using RPA.Infrastructure.Persistence;

/// <summary>
/// WP-6.2 — Action Center: bekleyen kayıtların listelenmesi, atama, çözümleme + not.
/// Spec Bölüm 8.2 (Action Center), 6 (BusinessException insan incelemesi).
/// </summary>
public class ActionCenterServiceTests
{
    private static RpaDbContext Db()
    {
        var options = new DbContextOptionsBuilder<RpaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new RpaDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static ActionItem Item(string type, string status = "Pending") => new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        Status = status,
    };

    [Fact]
    public async Task ListPending_ReturnsOnlyPending_FilteredByType()
    {
        using var db = Db();
        db.ActionItems.AddRange(
            Item("BusinessException"),
            Item("BusinessException", status: "Resolved"),
            Item("Approval"));
        await db.SaveChangesAsync();

        var svc = new ActionCenterService(new EfActionItemRepository(db));
        var business = await svc.ListPendingAsync("BusinessException");

        Assert.Single(business);
        Assert.All(business, i => Assert.Equal("Pending", i.Status));
    }

    [Fact]
    public async Task ListPending_NoTypeFilter_ReturnsAllPending()
    {
        using var db = Db();
        db.ActionItems.AddRange(Item("BusinessException"), Item("Approval"), Item("OtpRequest", "Resolved"));
        await db.SaveChangesAsync();

        var svc = new ActionCenterService(new EfActionItemRepository(db));
        var pending = await svc.ListPendingAsync(null);

        Assert.Equal(2, pending.Count);
    }

    [Fact]
    public async Task Resolve_SetsStatusNoteAndTimestamp()
    {
        using var db = Db();
        var item = Item("BusinessException");
        db.ActionItems.Add(item);
        await db.SaveChangesAsync();

        var svc = new ActionCenterService(new EfActionItemRepository(db));
        var resolved = await svc.ResolveAsync(item.Id, "Malzeme elle açıldı.");

        Assert.NotNull(resolved);
        Assert.Equal("Resolved", resolved!.Status);
        Assert.Equal("Malzeme elle açıldı.", resolved.ResolutionNote);
        Assert.NotNull(resolved.ResolvedAt);

        // Kalıcı olduğunu doğrula
        var reread = await db.ActionItems.FindAsync(item.Id);
        Assert.Equal("Resolved", reread!.Status);
    }

    [Fact]
    public async Task Resolve_ReturnsNull_WhenMissing()
    {
        using var db = Db();
        var svc = new ActionCenterService(new EfActionItemRepository(db));
        Assert.Null(await svc.ResolveAsync(Guid.NewGuid(), "x"));
    }

    [Fact]
    public async Task Assign_SetsAssignedUser()
    {
        using var db = Db();
        var item = Item("Approval");
        db.ActionItems.Add(item);
        await db.SaveChangesAsync();
        var userId = Guid.NewGuid();

        var svc = new ActionCenterService(new EfActionItemRepository(db));
        var assigned = await svc.AssignAsync(item.Id, userId);

        Assert.Equal(userId, assigned!.AssignedUserId);
    }
}

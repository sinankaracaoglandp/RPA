namespace RPA.WebAPI.Tests;

using Microsoft.AspNetCore.Mvc;
using RPA.Domain.Entities;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.ActionCenter;
using RPA.WebAPI.Controllers;

/// <summary>
/// WP-6.2 — ActionCenterController: listeleme (type filtresi), atama, çözümleme, 404/400 davranışı.
/// Fake IActionItemRepository ile (WebAPI.Tests EF sağlayıcısına bağımlı değildir).
/// </summary>
public class ActionCenterControllerTests
{
    private sealed class FakeRepo : IActionItemRepository
    {
        public readonly List<ActionItem> Items = new();
        public int SaveCount;

        public Task<IReadOnlyList<ActionItem>> ListPendingAsync(string? type, CancellationToken ct = default)
        {
            IEnumerable<ActionItem> q = Items.Where(a => a.Status == "Pending");
            if (!string.IsNullOrWhiteSpace(type)) q = q.Where(a => a.Type == type);
            return Task.FromResult<IReadOnlyList<ActionItem>>(q.ToList());
        }

        public Task<ActionItem?> FindByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(Items.FirstOrDefault(a => a.Id == id));

        public Task SaveChangesAsync(CancellationToken ct = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private static (ActionCenterController controller, FakeRepo repo) Create()
    {
        var repo = new FakeRepo();
        return (new ActionCenterController(new ActionCenterService(repo)), repo);
    }

    [Fact]
    public async Task ListPending_FiltersByType()
    {
        var (controller, repo) = Create();
        repo.Items.Add(new ActionItem { Type = "BusinessException", Status = "Pending" });
        repo.Items.Add(new ActionItem { Type = "Approval", Status = "Pending" });

        var result = await controller.ListPending("Approval", default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var items = Assert.IsType<List<ActionItemDto>>(ok.Value);
        Assert.Single(items);
        Assert.Equal("Approval", items[0].Type);
    }

    [Fact]
    public async Task Resolve_SetsResolvedAndReturnsDto()
    {
        var (controller, repo) = Create();
        var item = new ActionItem { Type = "BusinessException", Status = "Pending" };
        repo.Items.Add(item);

        var result = await controller.Resolve(item.Id, new ResolveActionItemRequest { Note = "çözüldü" }, default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<ActionItemDto>(ok.Value);
        Assert.Equal("Resolved", dto.Status);
        Assert.Equal("çözüldü", dto.ResolutionNote);
        Assert.Equal(1, repo.SaveCount);
    }

    [Fact]
    public async Task Resolve_ReturnsNotFound_WhenMissing()
    {
        var (controller, _) = Create();
        var result = await controller.Resolve(Guid.NewGuid(), new ResolveActionItemRequest(), default);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Assign_ReturnsBadRequest_WhenUserIdEmpty()
    {
        var (controller, repo) = Create();
        var item = new ActionItem { Type = "Approval", Status = "Pending" };
        repo.Items.Add(item);

        var result = await controller.Assign(item.Id, new AssignActionItemRequest { UserId = Guid.Empty }, default);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Assign_SetsUser_WhenValid()
    {
        var (controller, repo) = Create();
        var item = new ActionItem { Type = "Approval", Status = "Pending" };
        repo.Items.Add(item);
        var userId = Guid.NewGuid();

        var result = await controller.Assign(item.Id, new AssignActionItemRequest { UserId = userId }, default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<ActionItemDto>(ok.Value);
        Assert.Equal(userId, dto.AssignedUserId);
    }
}

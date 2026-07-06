namespace RPA.WebAPI.Tests;

using Microsoft.AspNetCore.Mvc;
using RPA.Domain.Entities;
using RPA.Domain.Interfaces;
using RPA.WebAPI.Controllers;

/// <summary>WP-6.3 — AlertRulesController: listeleme, oluşturma (validasyon), aktif değiştirme.</summary>
public class AlertRulesControllerTests
{
    private sealed class FakeRepo : IAlertRuleRepository
    {
        public readonly List<AlertRule> Rules = new();
        public Task<IReadOnlyList<AlertRule>> ListActiveAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AlertRule>>(Rules.Where(r => r.IsActive).ToList());
        public Task<IReadOnlyList<AlertRule>> ListAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AlertRule>>(Rules.ToList());
        public Task<AlertRule?> FindByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(Rules.FirstOrDefault(r => r.Id == id));
        public Task AddAsync(AlertRule rule, CancellationToken ct = default) { Rules.Add(rule); return Task.CompletedTask; }
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task Create_ReturnsBadRequest_WhenMissingFields()
    {
        var controller = new AlertRulesController(new FakeRepo());
        var result = await controller.Create(new CreateAlertRuleRequest { Name = "x" }, default);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Create_PersistsRule()
    {
        var repo = new FakeRepo();
        var controller = new AlertRulesController(repo);

        var result = await controller.Create(new CreateAlertRuleRequest
        {
            Name = "SysExc",
            Condition = "{\"metric\":\"SystemExceptionCount\",\"threshold\":5}",
            Channel = "email",
            Recipients = "ops@example.com",
        }, default);

        Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Single(repo.Rules);
        Assert.Equal("SysExc", repo.Rules[0].Name);
    }

    [Fact]
    public async Task SetActive_TogglesFlag()
    {
        var repo = new FakeRepo();
        var rule = new AlertRule { Name = "r", Channel = "email", IsActive = true };
        repo.Rules.Add(rule);
        var controller = new AlertRulesController(repo);

        var result = await controller.SetActive(rule.Id, new SetActiveRequest { IsActive = false }, default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<AlertRuleDto>(ok.Value);
        Assert.False(dto.IsActive);
    }

    [Fact]
    public async Task SetActive_ReturnsNotFound_WhenMissing()
    {
        var controller = new AlertRulesController(new FakeRepo());
        var result = await controller.SetActive(Guid.NewGuid(), new SetActiveRequest { IsActive = true }, default);
        Assert.IsType<NotFoundResult>(result.Result);
    }
}

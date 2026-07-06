namespace RPA.WebAPI.Tests;

using Microsoft.AspNetCore.Mvc;
using RPA.Domain.Entities;
using RPA.Domain.Interfaces;
using RPA.WebAPI.Controllers;

/// <summary>
/// JobRunsController testleri (WP-6.1 — Orchestrator İşler + Dashboard uçları).
/// Read-side mantığı repository'de test edilir (JobRunQueryTests); burada controller'ın
/// DTO eşleme + 404 + parametre geçişi davranışı doğrulanır.
/// </summary>
public class JobRunsControllerTests
{
    private sealed class FakeQueryRepo : IJobRunQueryRepository
    {
        public JobRunQuery? LastQuery;
        public JobRunPage Page = new(Array.Empty<JobRun>(), 0);
        public JobRun? Single;
        public DashboardSummary Summary = new(0, 0, 0, 0, 0, 0, 0);

        public Task<JobRunPage> ListAsync(JobRunQuery query, CancellationToken ct = default)
        {
            LastQuery = query;
            return Task.FromResult(Page);
        }

        public Task<JobRun?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(Single);

        public Task<DashboardSummary> GetDashboardSummaryAsync(
            DateTime dayUtc, Guid? environmentId, CancellationToken ct = default)
            => Task.FromResult(Summary);
    }

    [Fact]
    public async Task List_MapsItems_AndPassesFilters()
    {
        var repo = new FakeQueryRepo
        {
            Page = new JobRunPage(new[]
            {
                new JobRun { Id = Guid.NewGuid(), Status = "Successful", TriggeredBy = "cron" },
            }, TotalCount: 1),
        };
        var controller = new JobRunsController(repo);

        var result = await controller.List(
            status: "Successful", environmentId: null, robotId: null,
            fromUtc: null, toUtc: null, skip: 0, take: 25);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<JobRunListResponse>(ok.Value);
        Assert.Equal(1, body.TotalCount);
        Assert.Single(body.Items);
        Assert.Equal("Successful", body.Items[0].Status);
        Assert.Equal("Successful", repo.LastQuery!.Status);
        Assert.Equal(25, repo.LastQuery.Take);
    }

    [Fact]
    public async Task List_ClampsInvalidTake_ToDefault()
    {
        var repo = new FakeQueryRepo();
        var controller = new JobRunsController(repo);

        await controller.List(null, null, null, null, null, skip: 0, take: 9999);

        Assert.Equal(50, repo.LastQuery!.Take);
    }

    [Fact]
    public async Task Get_ReturnsNotFound_WhenMissing()
    {
        var controller = new JobRunsController(new FakeQueryRepo { Single = null });

        var result = await controller.Get(Guid.NewGuid(), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Get_ReturnsDto_WhenFound()
    {
        var id = Guid.NewGuid();
        var repo = new FakeQueryRepo { Single = new JobRun { Id = id, Status = "Running" } };
        var controller = new JobRunsController(repo);

        var result = await controller.Get(id, default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<JobRunDto>(ok.Value);
        Assert.Equal(id, dto.Id);
        Assert.Equal("Running", dto.Status);
    }

    [Fact]
    public async Task Dashboard_ReturnsSummary()
    {
        var repo = new FakeQueryRepo { Summary = new DashboardSummary(10, 2, 6, 1, 1, 0, 75.0) };
        var controller = new JobRunsController(repo);

        var result = await controller.Dashboard(dayUtc: null, environmentId: null);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var summary = Assert.IsType<DashboardSummary>(ok.Value);
        Assert.Equal(10, summary.Total);
        Assert.Equal(75.0, summary.SuccessRate);
    }
}

namespace RPA.Infrastructure.Tests;

using Microsoft.EntityFrameworkCore;
using RPA.Domain.Entities;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Persistence;

/// <summary>
/// JobRun read-side sorgu testleri (WP-6.1a — Orchestrator İşler ekranı + Dashboard).
/// Spec Bölüm 8.2: dashboard (bugünkü işler, başarı oranı), işler listesi + detay.
/// </summary>
public class JobRunQueryTests
{
    private static readonly Guid EnvProd = Guid.NewGuid();

    private static RpaDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<RpaDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var db = new RpaDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static JobRun Job(string status, DateTime startedAt, Guid? env = null) => new()
    {
        Id = Guid.NewGuid(),
        WorkflowVersionId = Guid.NewGuid(),
        EnvironmentId = env ?? EnvProd,
        Status = status,
        StartedAt = startedAt,
        TriggeredBy = "manual",
        ElasticsearchCorrelationId = Guid.NewGuid().ToString(),
    };

    [Fact]
    public async Task ListAsync_FiltersByStatus_AndOrdersByStartedAtDescending()
    {
        var db = CreateInMemoryDb();
        var now = DateTime.UtcNow;
        db.JobRuns.AddRange(
            Job("Successful", now.AddMinutes(-30)),
            Job("Failed", now.AddMinutes(-20)),
            Job("Successful", now.AddMinutes(-10)));
        await db.SaveChangesAsync();

        var repo = new EfJobRunQueryRepository(db);
        var page = await repo.ListAsync(new JobRunQuery(Status: "Successful"));

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
        // En yeni önce
        Assert.True(page.Items[0].StartedAt > page.Items[1].StartedAt);
    }

    [Fact]
    public async Task ListAsync_Paginates_WithSkipTake()
    {
        var db = CreateInMemoryDb();
        var now = DateTime.UtcNow;
        for (var i = 0; i < 5; i++)
        {
            db.JobRuns.Add(Job("Running", now.AddMinutes(-i)));
        }
        await db.SaveChangesAsync();

        var repo = new EfJobRunQueryRepository(db);
        var page = await repo.ListAsync(new JobRunQuery(Skip: 2, Take: 2));

        Assert.Equal(5, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenMissing()
    {
        var db = CreateInMemoryDb();
        var repo = new EfJobRunQueryRepository(db);

        Assert.Null(await repo.GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetDashboardSummaryAsync_CountsTodayAndComputesSuccessRate()
    {
        var db = CreateInMemoryDb();
        var today = DateTime.UtcNow.Date.AddHours(12);
        var yesterday = today.AddDays(-1);
        db.JobRuns.AddRange(
            Job("Successful", today),
            Job("Successful", today.AddMinutes(1)),
            Job("Failed", today.AddMinutes(2)),
            Job("Running", today.AddMinutes(3)),
            Job("Successful", yesterday)); // dünkü — sayılmamalı
        await db.SaveChangesAsync();

        var repo = new EfJobRunQueryRepository(db);
        var summary = await repo.GetDashboardSummaryAsync(today, environmentId: null);

        Assert.Equal(4, summary.Total);
        Assert.Equal(2, summary.Successful);
        Assert.Equal(1, summary.Failed);
        Assert.Equal(1, summary.Running);
        // Başarı oranı = tamamlanan içinde başarılı = 2 / (2+1) = %66.67 (Running hariç)
        Assert.Equal(66.67, Math.Round(summary.SuccessRate, 2));
    }
}

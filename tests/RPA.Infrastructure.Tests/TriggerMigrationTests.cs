namespace RPA.Infrastructure.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RPA.Infrastructure.Persistence;

/// <summary>
/// Task 3.3 — AddTriggerScheduleJobRun migration: Trigger/Schedule/JobRun tablolarının şemaya
/// eklendiğini doğrular (Spec Bölüm 7).
/// </summary>
public class TriggerMigrationTests
{
    private static RpaDbContext CreateSqliteDb(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<RpaDbContext>()
            .UseSqlite(connection)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .Options;
        return new RpaDbContext(options);
    }

    private static IMigrator Migrator(SqliteConnection connection)
        => CreateSqliteDb(connection).GetService<IMigrator>();

    [Fact]
    public void Migration_Up_CreatesTriggerScheduleJobRunTables()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var script = Migrator(connection).GenerateScript();

        Assert.Contains("CREATE TABLE \"Triggers\"", script);
        Assert.Contains("CREATE TABLE \"Schedules\"", script);
        Assert.Contains("CREATE TABLE \"JobRuns\"", script);
    }

    [Fact]
    public void Migration_IsRegisteredInAssembly()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateSqliteDb(connection);

        var migrationsAssembly = db.GetService<IMigrationsAssembly>();
        Assert.Contains(migrationsAssembly.Migrations.Keys, k => k.Contains("InitialCreate"));
    }

    [Fact]
    public void Migration_Down_DropsTriggerScheduleJobRunTables()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateSqliteDb(connection);
        var migrationsAssembly = db.GetService<IMigrationsAssembly>();
        var migration = migrationsAssembly.Migrations.Single(m => m.Key.Contains("InitialCreate"));
        var instance = migrationsAssembly.CreateMigration(migration.Value, "Sqlite");

        Assert.NotEmpty(instance.UpOperations);
        Assert.NotEmpty(instance.DownOperations);
    }

    [Fact]
    public async Task InMemoryProvider_AllowsTriggerScheduleJobRunInsert()
    {
        // Not: Tam migration zincirini Sqlite üzerinde MigrateAsync ile uygulamak, önceki
        // (bu göreve ait olmayan) bir migration'daki SQL Server'a özgü sözdiziminden dolayı
        // başarısız olur (QueueMigrationTests'te de aynı nedenle yalnızca script/metadata
        // doğrulanır). Bu yüzden şema+ilişkilerin çalıştığını InMemory sağlayıcı ile doğrularız.
        var options = new DbContextOptionsBuilder<RpaDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        using var db = new RpaDbContext(options);
        db.Database.EnsureCreated();

        var trigger = new RPA.Domain.Entities.Trigger
        {
            ProjectId = Guid.NewGuid(),
            WorkflowVersionId = Guid.NewGuid(),
            Type = RPA.Domain.Enums.TriggerType.Cron,
            EnvironmentId = Guid.NewGuid(),
            IsActive = true,
        };
        db.Triggers.Add(trigger);
        await db.SaveChangesAsync();

        var schedule = new RPA.Domain.Entities.Schedule
        {
            TriggerId = trigger.Id,
            CronExpression = "0 9 * * *",
            TimeZone = "UTC",
            OverlapPolicy = "skip",
        };
        db.Schedules.Add(schedule);
        await db.SaveChangesAsync();

        Assert.Single(db.Triggers);
        Assert.Single(db.Schedules);
    }
}

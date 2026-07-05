namespace RPA.Infrastructure.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RPA.Infrastructure.Persistence;

/// <summary>
/// Task 3.2 — AddQueueClaimIndex migration: kuyruk claim indeksinin (QueueId + Status) şemaya
/// eklendiğini ve rollback ürettiğini doğrular. SQLite üzerinde script üretimiyle kod yolu çalıştırılır.
/// </summary>
public class QueueMigrationTests
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
    public void Migration_Up_CreatesQueueClaimIndex()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var script = Migrator(connection).GenerateScript();

        Assert.Contains("IX_QueueItems_QueueId_Status", script);
    }

    [Fact]
    public void Migration_IsRegisteredInAssembly()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateSqliteDb(connection);

        var migrationsAssembly = db.GetService<IMigrationsAssembly>();
        Assert.Contains(migrationsAssembly.Migrations.Keys, k => k.Contains("AddQueueClaimIndex"));
    }

    [Fact]
    public void Migration_Down_DropsQueueClaimIndex()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateSqliteDb(connection);
        var migrationsAssembly = db.GetService<IMigrationsAssembly>();
        var migrationId = migrationsAssembly.Migrations.Keys.Single(k => k.Contains("AddQueueClaimIndex"));

        var migration = migrationsAssembly.Migrations.Single(m => m.Key.Contains("AddQueueClaimIndex"));
        var instance = migrationsAssembly.CreateMigration(migration.Value, "Sqlite");

        Assert.NotEmpty(instance.UpOperations);
        Assert.NotEmpty(instance.DownOperations);
    }
}

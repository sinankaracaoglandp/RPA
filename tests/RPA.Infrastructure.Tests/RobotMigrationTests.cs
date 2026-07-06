namespace RPA.Infrastructure.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RPA.Infrastructure.Persistence;

/// <summary>
/// Task 3.1 — EF migration (AddRobot) uygulama / rollback / şema doğrulama testleri.
/// Migration Up/Down ve hedef model (BuildTargetModel) kod yolları script üretimiyle çalıştırılır.
/// SQLite üzerinde canlı DDL çalıştırılamaz (nvarchar(max) SqlServer'a özgü) — bu yüzden
/// script üretimi (kod yolunu çalıştıran ama DDL execute etmeyen) tercih edilir.
/// </summary>
public class RobotMigrationTests
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
    public void Migration_Up_GeneratesFullSchema()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var script = Migrator(connection).GenerateScript();

        // Up() kod yolu tüm CreateTable/CreateIndex operasyonlarını üretir.
        Assert.Contains("Robots", script);
        Assert.Contains("MachineName", script);
        Assert.Contains("IX_Robots_MachineName", script);
        Assert.Contains("QueueItems", script);
        Assert.Contains("IX_QueueItems_QueueId_IdempotencyKey", script);
        Assert.Contains("AuditLogs", script);
        Assert.Contains("Queues", script);
        Assert.Contains("Users", script);
    }

    [Fact]
    public void Migration_Down_GeneratesRollbackScript()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var migrationId = MigrationId();
        // Rollback: AddRobot -> InitialDatabase ("0"), Down() kod yolunu çalıştırır (DropTable).
        var downScript = Migrator(connection).GenerateScript(
            fromMigration: migrationId,
            toMigration: Migration.InitialDatabase);

        Assert.Contains("DROP TABLE", downScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Robots", downScript);
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
    public void Migration_TargetModel_ContainsRobotEntity()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateSqliteDb(connection);

        var migrationsAssembly = db.GetService<IMigrationsAssembly>();
        var migration = migrationsAssembly.Migrations
            .Single(m => m.Key.Contains("InitialCreate"));
        var instance = migrationsAssembly.CreateMigration(migration.Value, "Sqlite");

        // BuildTargetModel kod yolunu tetikler ve Robot varlığını doğrular.
        var model = instance.TargetModel;
        Assert.NotNull(model.FindEntityType(typeof(RPA.Domain.Entities.Robot)));
        Assert.NotEmpty(instance.UpOperations);
        Assert.NotEmpty(instance.DownOperations);
    }

    private static string MigrationId()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateSqliteDb(connection);
        var migrationsAssembly = db.GetService<IMigrationsAssembly>();
        return migrationsAssembly.Migrations.Keys.Single(k => k.Contains("InitialCreate"));
    }
}

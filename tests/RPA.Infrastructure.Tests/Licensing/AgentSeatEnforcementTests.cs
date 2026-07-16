using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Exceptions;
using RPA.Domain.Licensing;
using RPA.Infrastructure.Licensing;
using RPA.Infrastructure.Persistence;
using RPA.Infrastructure.Persistence.Repositories;

namespace RPA.Infrastructure.Tests.Licensing;

public sealed class AgentSeatEnforcementTests
{
    [Fact]
    public async Task ActivateAsync_WithZeroOfOneSeats_ConsumesCodeAndStoresOnlyHashes()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (installation, agent, activation) = await SeedAsync(database, 1);
        await using var db = database.CreateContext();
        var repository = new EfAgentIdentityRepository(db);

        await repository.ActivateWithCodeAsync(installation.Id, activation.ActivationCodeHash,
            agent.Id, "machine-1", "credential-hash", DateTimeOffset.UtcNow);

        db.ChangeTracker.Clear();
        var savedAgent = await db.AgentIdentities.SingleAsync(x => x.Id == agent.Id);
        var savedActivation = await db.AgentActivations.SingleAsync(x => x.Id == activation.Id);
        Assert.Equal(AgentIdentityStatus.Activated, savedAgent.Status);
        Assert.Equal("credential-hash", savedAgent.CredentialHash);
        Assert.NotNull(savedActivation.ConsumedAt);
        Assert.DoesNotContain("activation-plaintext", JsonSerializer.Serialize(db.ChangeTracker.Entries().Select(x => x.Entity)));
        Assert.DoesNotContain("credential-plaintext", JsonSerializer.Serialize(db.ChangeTracker.Entries().Select(x => x.Entity)));
    }

    [Fact]
    public async Task ActivateAsync_WithOneOfOneSeats_ThrowsStableLimitCode()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (installation, agent, activation) = await SeedAsync(database, 1, AgentIdentityStatus.Activated);
        await using var db = database.CreateContext();

        var error = await Assert.ThrowsAsync<BusinessException>(() =>
            new EfAgentIdentityRepository(db).ActivateWithCodeAsync(installation.Id,
                activation.ActivationCodeHash, agent.Id, "machine-2", "hash", DateTimeOffset.UtcNow));

        Assert.Equal("AGENT_LICENSE_LIMIT_REACHED", error.Message);
    }

    [Theory]
    [InlineData(AgentIdentityStatus.Disabled, false)]
    [InlineData(AgentIdentityStatus.Deactivated, true)]
    public async Task SeatAccounting_UsesSpecifiedStates(AgentIdentityStatus existingState, bool succeeds)
    {
        await using var database = await TestDatabase.CreateAsync();
        var (installation, agent, activation) = await SeedAsync(database, 1, existingState);
        await using var db = database.CreateContext();
        var task = new EfAgentIdentityRepository(db).ActivateWithCodeAsync(installation.Id,
            activation.ActivationCodeHash, agent.Id, "machine", "hash", DateTimeOffset.UtcNow);

        if (succeeds) await task;
        else await Assert.ThrowsAsync<BusinessException>(() => task);
    }

    [Fact]
    public async Task ActivationCode_IsSingleUse()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (installation, agent, activation) = await SeedAsync(database, 2);
        await using var first = database.CreateContext();
        await new EfAgentIdentityRepository(first).ActivateWithCodeAsync(installation.Id,
            activation.ActivationCodeHash, agent.Id, "machine", "hash", DateTimeOffset.UtcNow);
        await using var second = database.CreateContext();

        var error = await Assert.ThrowsAsync<BusinessException>(() =>
            new EfAgentIdentityRepository(second).ActivateWithCodeAsync(installation.Id,
                activation.ActivationCodeHash, agent.Id, "machine", "hash2", DateTimeOffset.UtcNow));
        Assert.Equal("ACTIVATION_CODE_INVALID", error.Message);
    }

    [Fact]
    public async Task ConcurrentFinalSeat_ExactlyOneActivationSucceeds()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (installation, firstAgent, firstCode) = await SeedAsync(database, 1);
        AgentIdentity secondAgent;
        AgentActivation secondCode;
        await using (var seed = database.CreateContext())
        {
            secondAgent = new AgentIdentity { LicenseInstallationId = installation.Id, Name = "second" };
            secondCode = new AgentActivation { AgentIdentityId = secondAgent.Id, ActivationCodeHash = "code-2", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15) };
            seed.AddRange(secondAgent, secondCode);
            await seed.SaveChangesAsync();
        }

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Activate(Guid agentId, string code, string machine)
        {
            await using var db = database.CreateContext();
            await start.Task;
            try
            {
                await new EfAgentIdentityRepository(db).ActivateWithCodeAsync(installation.Id, code, agentId, machine, "hash", DateTimeOffset.UtcNow);
                return true;
            }
            catch (BusinessException ex) when (ex.Message == "AGENT_LICENSE_LIMIT_REACHED") { return false; }
        }

        var attempts = new[] { Activate(firstAgent.Id, firstCode.ActivationCodeHash, "m1"), Activate(secondAgent.Id, secondCode.ActivationCodeHash, "m2") };
        start.SetResult();
        Assert.Equal(1, (await Task.WhenAll(attempts)).Count(x => x));
    }

    private static async Task<(LicenseInstallation Installation, AgentIdentity Candidate, AgentActivation Code)> SeedAsync(
        TestDatabase database, int maxSeats, AgentIdentityStatus? existingState = null)
    {
        await using var db = database.CreateContext();
        var payload = OfflineLicensePayload.Create("LIC", 1, "customer", "Customer Ltd.", "enterprise", "install", "fingerprint", maxSeats,
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), []);
        var installation = new LicenseInstallation
        {
            InstallationId = "install", PublicKey = "key", PublicKeyFingerprint = "fingerprint", ProductId = "product",
            InstallationCreatedAt = DateTimeOffset.UtcNow, SignedLicenseDocument = JsonSerializer.Serialize(new SignedLicenseDocument(payload, "signature")), InstalledLicenseRevision = 1
        };
        if (existingState.HasValue)
            db.AgentIdentities.Add(new AgentIdentity { LicenseInstallationId = installation.Id, Name = "existing", Status = existingState.Value });
        var candidate = new AgentIdentity { LicenseInstallationId = installation.Id, Name = "candidate" };
        var activation = new AgentActivation { AgentIdentityId = candidate.Id, ActivationCodeHash = "code-1", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15) };
        db.AddRange(installation, candidate, activation);
        await db.SaveChangesAsync();
        return (installation, candidate, activation);
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _anchor;
        private readonly string _connectionString;
        private TestDatabase(SqliteConnection anchor, string connectionString) { _anchor = anchor; _connectionString = connectionString; }
        public static async Task<TestDatabase> CreateAsync()
        {
            var cs = $"Data Source=seat-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Default Timeout=10";
            var anchor = new SqliteConnection(cs);
            await anchor.OpenAsync();
            var result = new TestDatabase(anchor, cs);
            await using var db = result.CreateContext();
            await db.Database.EnsureCreatedAsync();
            return result;
        }
        public RpaDbContext CreateContext() => new(new DbContextOptionsBuilder<RpaDbContext>().UseSqlite(_connectionString).Options);
        public ValueTask DisposeAsync() => _anchor.DisposeAsync();
    }
}

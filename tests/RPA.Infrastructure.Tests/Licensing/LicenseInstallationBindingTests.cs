using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RPA.Domain.Entities;
using RPA.Domain.Exceptions;
using RPA.Domain.Licensing;
using RPA.Infrastructure.Licensing;
using RPA.Infrastructure.Persistence;

namespace RPA.Infrastructure.Tests.Licensing;

/// <summary>
/// Lisansin KURULUM KIMLIGINE baglanmasi (review bulgusu): imza dogru olsa bile lisans, yalnizca
/// uzerinde uretildigi kurulumda gecerlidir. Aksi halde veritabanini baska bir sunucuya kopyalamak
/// lisansi bedavaya cogaltirdi — LICENSE_INSTALLATION_MISMATCH yalnizca ImportAsync'te uygulaniyordu.
/// </summary>
public sealed class LicenseInstallationBindingTests
{
    [Fact]
    public async Task GetStatusAsync_ForeignInstallationDocument_IsNotValid()
    {
        await using var database = await TestDatabase.CreateAsync();
        var identity = await IdentityOf("RPA.Platform");
        await SeedInstallationAsync(database, identity, payloadInstallationId: "SOMEONE-ELSE",
            payloadFingerprint: identity.PublicKeyFingerprint);
        await using var db = database.CreateContext();

        var status = await CreateService(db, identity).GetStatusAsync();

        Assert.False(status.IsValid);
        Assert.Equal("LICENSE_INSTALLATION_MISMATCH", status.ErrorCode);
    }

    [Fact]
    public async Task GetStatusAsync_ClonedDatabaseOnDifferentMachine_IsNotValid()
    {
        await using var database = await TestDatabase.CreateAsync();
        var original = await IdentityOf("RPA.Platform");
        await SeedInstallationAsync(database, original, original.InstallationId, original.PublicKeyFingerprint);

        // Veritabani kopyalandi ama DPAPI anahtari tasinmadi → yeni makinede kimlik farklidir.
        var clonedMachine = await IdentityOf("RPA.Platform");
        Assert.NotEqual(original.InstallationId, clonedMachine.InstallationId);
        await using var db = database.CreateContext();

        var status = await CreateService(db, clonedMachine).GetStatusAsync();

        Assert.False(status.IsValid);
        Assert.False(status.IsInstalled);
        Assert.Equal("LICENSE_MISSING", status.ErrorCode);
    }

    [Fact]
    public async Task EnsureAgentCapacityAsync_ClonedDatabase_Throws()
    {
        await using var database = await TestDatabase.CreateAsync();
        var original = await IdentityOf("RPA.Platform");
        await SeedInstallationAsync(database, original, original.InstallationId, original.PublicKeyFingerprint);
        var clonedMachine = await IdentityOf("RPA.Platform");
        await using var db = database.CreateContext();

        var error = await Assert.ThrowsAsync<BusinessException>(
            () => CreateService(db, clonedMachine).EnsureAgentCapacityAsync());

        Assert.Equal("LICENSE_MISSING", error.Message);
    }

    [Fact]
    public async Task GetStatusAsync_SecondInstallationRow_SelectsCurrentIdentityInsteadOfThrowing()
    {
        await using var database = await TestDatabase.CreateAsync();
        var stale = await IdentityOf("RPA.Platform");
        var current = await IdentityOf("RPA.Platform");
        await SeedInstallationAsync(database, stale, stale.InstallationId, stale.PublicKeyFingerprint);
        await SeedInstallationAsync(database, current, current.InstallationId, current.PublicKeyFingerprint);
        await using var db = database.CreateContext();

        var status = await CreateService(db, current).GetStatusAsync();

        Assert.True(status.IsValid);
        Assert.Equal("LIC", status.LicenseId);
    }

    [Fact]
    public async Task GetStatusAsync_MatchingInstallation_RemainsValid()
    {
        await using var database = await TestDatabase.CreateAsync();
        var identity = await IdentityOf("RPA.Platform");
        await SeedInstallationAsync(database, identity, identity.InstallationId, identity.PublicKeyFingerprint);
        await using var db = database.CreateContext();

        var status = await CreateService(db, identity).GetStatusAsync();

        Assert.True(status.IsInstalled);
        Assert.True(status.IsValid);
        Assert.Null(status.ErrorCode);
    }

    private static LicenseService CreateService(RpaDbContext db, InstallationIdentity identity) =>
        new(db, new FixedIdentityService(identity), new AlwaysValidVerifier(), "RPA.Platform");

    private static async Task<InstallationIdentity> IdentityOf(string productId) =>
        await new InstallationIdentityService(new MemoryKeyStore(), productId).GetOrCreateAsync();

    private static async Task SeedInstallationAsync(
        TestDatabase database, InstallationIdentity identity, string payloadInstallationId, string payloadFingerprint)
    {
        await using var db = database.CreateContext();
        var payload = OfflineLicensePayload.Create("LIC", 1, "customer", "Customer Ltd.", "enterprise",
            payloadInstallationId, payloadFingerprint, 5,
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), []);
        db.Add(new LicenseInstallation
        {
            InstallationId = identity.InstallationId,
            PublicKey = identity.PublicKey,
            PublicKeyFingerprint = identity.PublicKeyFingerprint,
            ProductId = "RPA.Platform",
            InstallationCreatedAt = DateTimeOffset.UtcNow,
            SignedLicenseDocument = JsonSerializer.Serialize(new SignedLicenseDocument(payload, "signature")),
            InstalledLicenseRevision = 1,
        });
        await db.SaveChangesAsync();
    }

    private sealed class FixedIdentityService(InstallationIdentity identity) : IInstallationIdentityService
    {
        public Task<InstallationIdentity> GetOrCreateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(identity);
    }

    /// <summary>Imza dogrulamasini kapsam disi birakir — bu testler KURULUM baglantisini olcer.</summary>
    private sealed class AlwaysValidVerifier : IVendorLicenseVerifier
    {
        public bool Verify(SignedLicenseDocument document) => true;
    }

    private sealed class MemoryKeyStore : IInstallationKeyStore
    {
        private byte[]? _key;
        public Task<byte[]?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(_key?.ToArray());
        public Task<bool> TrySaveAsync(byte[] privateKey, CancellationToken cancellationToken = default)
        {
            if (_key is not null) return Task.FromResult(false);
            _key = privateKey.ToArray();
            return Task.FromResult(true);
        }
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _anchor;
        private readonly string _connectionString;
        private TestDatabase(SqliteConnection anchor, string connectionString)
        { _anchor = anchor; _connectionString = connectionString; }

        public static async Task<TestDatabase> CreateAsync()
        {
            var cs = $"Data Source=binding-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Default Timeout=10";
            var anchor = new SqliteConnection(cs);
            await anchor.OpenAsync();
            var result = new TestDatabase(anchor, cs);
            await using var db = result.CreateContext();
            await db.Database.EnsureCreatedAsync();
            return result;
        }

        public RpaDbContext CreateContext() =>
            new(new DbContextOptionsBuilder<RpaDbContext>().UseSqlite(_connectionString).Options);

        public ValueTask DisposeAsync() => _anchor.DisposeAsync();
    }
}

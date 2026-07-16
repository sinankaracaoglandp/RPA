using System.Security.Cryptography;
using System.Text;
using RPA.Domain.Licensing;
using RPA.Infrastructure.Licensing;

namespace RPA.Infrastructure.Tests.Licensing;

public class LicenseCryptographyTests
{
    [Fact]
    public void SerializePayload_IsDeterministicAndCanonical()
    {
        var first = Payload(features: ["Studio", "Agent", "Studio"]);
        var second = Payload(features: ["Agent", "Studio"]);

        var firstBytes = CanonicalLicenseSerializer.SerializePayload(first);
        var secondBytes = CanonicalLicenseSerializer.SerializePayload(second);

        Assert.Equal(firstBytes, secondBytes);
        Assert.Equal(
            "{\"schemaVersion\":1,\"licenseId\":\"LIC-1\",\"revision\":2,\"customerId\":\"ACME\",\"installationId\":\"install-1\",\"installationPublicKeyFingerprint\":\"ABC\",\"maxActivatedAgents\":5,\"issuedAt\":\"2026-01-01T00:00:00.0000000Z\",\"expiresAt\":\"2027-01-01T00:00:00.0000000Z\",\"features\":[\"Agent\",\"Studio\"]}",
            Encoding.UTF8.GetString(firstBytes));
    }

    [Fact]
    public void Verify_AcceptsUntouchedRsaPssSha256Document()
    {
        using var rsa = RSA.Create(2048);
        var payload = Payload();
        var signature = rsa.SignData(CanonicalLicenseSerializer.SerializePayload(payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        var verifier = new VendorLicenseVerifier(rsa.ExportSubjectPublicKeyInfoPem());

        Assert.True(verifier.Verify(new SignedLicenseDocument(payload, Convert.ToBase64String(signature))));
    }

    [Fact]
    public void Verify_RejectsPayloadAndSignatureTampering()
    {
        using var rsa = RSA.Create(2048);
        var payload = Payload();
        var signature = rsa.SignData(CanonicalLicenseSerializer.SerializePayload(payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        var verifier = new VendorLicenseVerifier(rsa.ExportSubjectPublicKeyInfoPem());
        var signed = new SignedLicenseDocument(payload, Convert.ToBase64String(signature));

        Assert.False(verifier.Verify(signed with { Payload = payload with { MaxActivatedAgents = 6 } }));
        Assert.False(verifier.Verify(signed with { Payload = payload with { InstallationPublicKeyFingerprint = "DEF" } }));
        signature[0] ^= 0xff;
        Assert.False(verifier.Verify(signed with { Signature = Convert.ToBase64String(signature) }));
    }

    [Fact]
    public async Task InstallationIdentity_GeneratesOnceAndReloadsProtectedPkcs8Key()
    {
        var store = new MemoryInstallationKeyStore();
        var service = new InstallationIdentityService(store, "RPA.Platform");

        var first = await service.GetOrCreateAsync();
        var second = await new InstallationIdentityService(store, "RPA.Platform").GetOrCreateAsync();

        Assert.Equal(first, second);
        Assert.NotEmpty(first.InstallationId);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(first.PublicKey))), first.PublicKeyFingerprint);
        Assert.NotNull(store.SavedKey);
    }

    [Fact]
    public async Task DpapiStore_UsesProtectionSeamAndAtomicFileReplacement()
    {
        var files = new MemoryInstallationFileSystem();
        var protection = new RecordingProtection();
        var store = new DpapiInstallationKeyStore("C:\\app-data", files, protection);
        byte[] key = [1, 2, 3, 4];

        await store.SaveAsync(key);
        var loaded = await store.LoadAsync();

        Assert.Equal(key, loaded);
        Assert.Equal(key, protection.ProtectedInput);
        Assert.Equal("C:\\app-data\\installation-key.pk8.protected", files.FinalPath);
        Assert.Equal("C:\\app-data\\installation-key.pk8.protected.tmp", files.TempPath);
    }

    private static OfflineLicensePayload Payload(IEnumerable<string>? features = null) =>
        new(1, "LIC-1", 2, "ACME", "install-1", "ABC", 5,
            DateTimeOffset.Parse("2026-01-01T03:00:00+03:00"),
            DateTimeOffset.Parse("2027-01-01T03:00:00+03:00"), features ?? ["Agent"]);

    private sealed class MemoryInstallationKeyStore : IInstallationKeyStore
    {
        public byte[]? SavedKey { get; private set; }
        public Task<byte[]?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(SavedKey?.ToArray());
        public Task SaveAsync(byte[] privateKey, CancellationToken cancellationToken = default)
        {
            SavedKey = privateKey.ToArray();
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingProtection : IInstallationKeyProtection
    {
        public byte[]? ProtectedInput { get; private set; }
        public byte[] Protect(byte[] plaintext)
        {
            ProtectedInput = plaintext.ToArray();
            return plaintext.Select(value => (byte)(value ^ 0x5a)).ToArray();
        }
        public byte[] Unprotect(byte[] protectedData) => protectedData.Select(value => (byte)(value ^ 0x5a)).ToArray();
    }

    private sealed class MemoryInstallationFileSystem : IInstallationFileSystem
    {
        private byte[]? _contents;
        public string? TempPath { get; private set; }
        public string? FinalPath { get; private set; }
        public bool Exists(string path) => _contents is not null && path == FinalPath;
        public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken) => Task.FromResult(_contents!.ToArray());
        public Task WriteAllBytesAsync(string path, byte[] contents, CancellationToken cancellationToken)
        {
            TempPath = path;
            _contents = contents.ToArray();
            return Task.CompletedTask;
        }
        public void CreateDirectory(string path) { }
        public void MoveAtomically(string temporaryPath, string destinationPath)
        {
            TempPath = temporaryPath;
            FinalPath = destinationPath;
        }
    }
}

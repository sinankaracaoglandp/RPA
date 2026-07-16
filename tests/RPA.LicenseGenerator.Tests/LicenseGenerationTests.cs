using System.Security.Cryptography;
using System.Text.Json;
using RPA.Infrastructure.Licensing;
using RPA.LicenseGenerator;

namespace RPA.LicenseGenerator.Tests;

public sealed class LicenseGenerationTests : IDisposable
{
    private const string PasswordEnvVar = "RPA_TEST_VENDOR_KEY_PASSWORD";
    private const string KeyPassword = "s3cr3t-vendor-passphrase";

    private readonly string _directory = Directory.CreateTempSubdirectory("rpa-licgen-").FullName;
    private readonly RSA _vendorKey = RSA.Create(2048);
    private readonly RSA _installationKey = RSA.Create(2048);

    public LicenseGenerationTests() => Environment.SetEnvironmentVariable(PasswordEnvVar, KeyPassword);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(PasswordEnvVar, null);
        _vendorKey.Dispose();
        _installationKey.Dispose();
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Generate_ProducesDocumentAcceptedByRuntimeVerifier()
    {
        var options = LicenseGenerationOptions.Parse(DefaultArguments());

        var document = LicenseGenerationService.Generate(options);

        var verifier = new VendorLicenseVerifier(_vendorKey.ExportSubjectPublicKeyInfoPem());
        Assert.True(verifier.Verify(document));
        Assert.Equal("LIC-1", document.Payload.LicenseId);
        Assert.Equal("enterprise", document.Payload.Edition);
        Assert.Equal("ACME Sanayi A.S.", document.Payload.CustomerName);
        Assert.Equal(5, document.Payload.MaxActivatedAgents);
        Assert.Equal(InstallationId(), document.Payload.InstallationId);
        Assert.Equal(Fingerprint(), document.Payload.InstallationPublicKeyFingerprint);
        Assert.Equal<string[]>(["Agent", "Studio"], [.. document.Payload.Features]);
    }

    [Fact]
    public void Generate_WritesSignedDocumentAtomicallyWithoutLeavingTemporaryFiles()
    {
        var options = LicenseGenerationOptions.Parse(DefaultArguments());

        var document = LicenseGenerationService.Generate(options);

        var written = JsonDocument.Parse(File.ReadAllText(OutputPath())).RootElement;
        Assert.Equal(document.Signature, written.GetProperty("Signature").GetString());
        Assert.Equal("RSA-PSS-SHA256", written.GetProperty("Algorithm").GetString());
        Assert.Equal("enterprise", written.GetProperty("Payload").GetProperty("Edition").GetString());
        Assert.Single(Directory.GetFiles(_directory, "*.lic*"));
    }

    [Fact]
    public void Generate_RejectsInvalidRequestDocument()
    {
        File.WriteAllText(RequestPath(), "{\"schemaVersion\":1,\"installationId\":\"\"}");
        var options = LicenseGenerationOptions.Parse(DefaultArguments(writeRequest: false));

        var error = Assert.Throws<LicenseGenerationException>(() => LicenseGenerationService.Generate(options));

        Assert.Contains("kurulum talebi", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(OutputPath()));
    }

    [Fact]
    public void Generate_RejectsRequestWhoseFingerprintDoesNotMatchItsPublicKey()
    {
        WriteRequest(fingerprint: "DEADBEEF");
        var options = LicenseGenerationOptions.Parse(DefaultArguments(writeRequest: false));

        Assert.Throws<LicenseGenerationException>(() => LicenseGenerationService.Generate(options));
    }

    [Fact]
    public void Parse_RejectsNonPositiveMaxAgents()
    {
        var error = Assert.Throws<LicenseGenerationException>(
            () => LicenseGenerationOptions.Parse(DefaultArguments(maxAgents: "0")));

        Assert.Contains("--max-agents", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsExpiryBeforeIssueDate()
    {
        var error = Assert.Throws<LicenseGenerationException>(
            () => LicenseGenerationOptions.Parse(DefaultArguments(issued: "2027-01-01", expires: "2026-01-01")));

        Assert.Contains("--expires", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--edition")]
    [InlineData("--customer-name")]
    public void Parse_RejectsMissingRequiredPayloadIdentity(string argument)
    {
        var withoutArgument = RemoveArgument(DefaultArguments(), argument);
        var blankArgument = SetArgument(DefaultArguments(), argument, "   ");

        Assert.Contains(argument, Assert.Throws<LicenseGenerationException>(
            () => LicenseGenerationOptions.Parse(withoutArgument)).Message, StringComparison.Ordinal);
        Assert.Contains(argument, Assert.Throws<LicenseGenerationException>(
            () => LicenseGenerationOptions.Parse(blankArgument)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsUnknownAndValuelessArguments()
    {
        Assert.Throws<LicenseGenerationException>(
            () => LicenseGenerationOptions.Parse([.. DefaultArguments(), "--unknown", "x"]));
        Assert.Throws<LicenseGenerationException>(
            () => LicenseGenerationOptions.Parse([.. DefaultArguments(), "--features"]));
    }

    [Fact]
    public void PrivateKeyLoader_FailureDisclosesNeitherPasswordNorKeyMaterial()
    {
        var keyPath = Path.Combine(_directory, "wrong.pem");
        var keyPem = _vendorKey.ExportEncryptedPkcs8PrivateKeyPem(
            "a-different-password", new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 100_000));
        File.WriteAllText(keyPath, keyPem);

        var error = Assert.Throws<LicenseGenerationException>(() => PrivateKeyLoader.Load(keyPath, PasswordEnvVar));
        var rendered = error.ToString();

        Assert.DoesNotContain(KeyPassword, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("a-different-password", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE KEY", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(keyPem, rendered, StringComparison.Ordinal);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void PrivateKeyLoader_RejectsMissingFileAndUnsetPasswordVariable()
    {
        Assert.Throws<LicenseGenerationException>(
            () => PrivateKeyLoader.Load(Path.Combine(_directory, "absent.pem"), PasswordEnvVar));
        Assert.Throws<LicenseGenerationException>(
            () => PrivateKeyLoader.Load(WriteVendorKey(), "RPA_TEST_UNSET_PASSWORD_VARIABLE"));
    }

    private string[] DefaultArguments(
        string maxAgents = "5",
        string issued = "2026-01-01",
        string expires = "2027-01-01",
        bool writeRequest = true)
    {
        if (writeRequest)
        {
            WriteRequest();
        }

        return
        [
            "generate",
            "--request", RequestPath(),
            "--output", OutputPath(),
            "--key", WriteVendorKey(),
            "--key-password-env", PasswordEnvVar,
            "--license-id", "LIC-1",
            "--customer-id", "ACME",
            "--customer-name", "ACME Sanayi A.S.",
            "--edition", "enterprise",
            "--max-agents", maxAgents,
            "--issued", issued,
            "--expires", expires,
            "--features", "Studio,Agent"
        ];
    }

    private static string[] RemoveArgument(string[] arguments, string name)
    {
        var index = Array.IndexOf(arguments, name);
        return [.. arguments.Take(index), .. arguments.Skip(index + 2)];
    }

    private static string[] SetArgument(string[] arguments, string name, string value)
    {
        var copy = arguments.ToArray();
        copy[Array.IndexOf(copy, name) + 1] = value;
        return copy;
    }

    private string RequestPath() => Path.Combine(_directory, "installation-request.json");

    private string OutputPath() => Path.Combine(_directory, "customer.lic");

    private string WriteVendorKey()
    {
        var keyPath = Path.Combine(_directory, "vendor.pem");
        File.WriteAllText(keyPath, _vendorKey.ExportEncryptedPkcs8PrivateKeyPem(
            KeyPassword, new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 100_000)));
        return keyPath;
    }

    private void WriteRequest(string? fingerprint = null) =>
        File.WriteAllText(RequestPath(), JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            installationId = InstallationId(),
            installationPublicKey = PublicKeyBase64(),
            installationPublicKeyFingerprint = fingerprint ?? Fingerprint(),
            productId = "RPA.Platform",
            customerReference = "ACME",
            createdAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z")
        }));

    private string PublicKeyBase64() => Convert.ToBase64String(_installationKey.ExportSubjectPublicKeyInfo());

    private string Fingerprint() => Convert.ToHexString(SHA256.HashData(_installationKey.ExportSubjectPublicKeyInfo()));

    private string InstallationId() => "INSTALL-" + Fingerprint()[..16];
}

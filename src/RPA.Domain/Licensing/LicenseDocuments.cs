using System.Collections.Immutable;

namespace RPA.Domain.Licensing;

public sealed record OfflineLicensePayload
{
    public OfflineLicensePayload(
        int schemaVersion,
        string licenseId,
        int revision,
        string customerId,
        string installationId,
        string installationPublicKeyFingerprint,
        int maxActivatedAgents,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        IEnumerable<string> features)
    {
        SchemaVersion = schemaVersion;
        LicenseId = licenseId;
        Revision = revision;
        CustomerId = customerId;
        InstallationId = installationId;
        InstallationPublicKeyFingerprint = installationPublicKeyFingerprint;
        MaxActivatedAgents = maxActivatedAgents;
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
        Features = features.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();
    }

    public int SchemaVersion { get; init; }
    public string LicenseId { get; init; }
    public int Revision { get; init; }
    public string CustomerId { get; init; }
    public string InstallationId { get; init; }
    public string InstallationPublicKeyFingerprint { get; init; }
    public int MaxActivatedAgents { get; init; }
    public DateTimeOffset IssuedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public ImmutableArray<string> Features { get; init; }

    public static OfflineLicensePayload Create(
        string licenseId,
        int revision,
        string customerId,
        string installationId,
        string installationPublicKeyFingerprint,
        int maxActivatedAgents,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        IEnumerable<string> features) =>
        new(1, licenseId, revision, customerId, installationId, installationPublicKeyFingerprint,
            maxActivatedAgents, issuedAt, expiresAt, features);
}

public sealed record SignedLicenseDocument(
    OfflineLicensePayload Payload,
    string Signature,
    string Algorithm = "RSA-PSS-SHA256");

public sealed record InstallationRequestDocument(
    int SchemaVersion,
    string InstallationId,
    string InstallationPublicKey,
    string InstallationPublicKeyFingerprint,
    string ProductId,
    string? CustomerReference,
    DateTimeOffset CreatedAt);

public sealed record LicenseStatus
{
    public LicenseStatus(
        bool isInstalled,
        bool isValid,
        string? licenseId,
        int? revision,
        string? customerId,
        DateTimeOffset? expiresAt,
        int maxActivatedAgents,
        int activatedAgents,
        IEnumerable<string> features,
        string? errorCode = null)
    {
        IsInstalled = isInstalled;
        IsValid = isValid;
        LicenseId = licenseId;
        Revision = revision;
        CustomerId = customerId;
        ExpiresAt = expiresAt;
        MaxActivatedAgents = maxActivatedAgents;
        ActivatedAgents = activatedAgents;
        Features = features.ToImmutableArray();
        ErrorCode = errorCode;
    }

    public bool IsInstalled { get; init; }
    public bool IsValid { get; init; }
    public string? LicenseId { get; init; }
    public int? Revision { get; init; }
    public string? CustomerId { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public int MaxActivatedAgents { get; init; }
    public int ActivatedAgents { get; init; }
    public ImmutableArray<string> Features { get; init; }
    public string? ErrorCode { get; init; }
}

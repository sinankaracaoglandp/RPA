namespace RPA.Domain.Licensing;

public sealed record OfflineLicensePayload(
    int SchemaVersion,
    string LicenseId,
    int Revision,
    string CustomerId,
    string InstallationId,
    string InstallationPublicKeyFingerprint,
    int MaxActivatedAgents,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<string> Features)
{
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
            maxActivatedAgents, issuedAt, expiresAt, features.Order(StringComparer.Ordinal).ToArray());
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

public sealed record LicenseStatus(
    bool IsInstalled,
    bool IsValid,
    string? LicenseId,
    int? Revision,
    string? CustomerId,
    DateTimeOffset? ExpiresAt,
    int MaxActivatedAgents,
    int ActivatedAgents,
    IReadOnlyList<string> Features,
    string? ErrorCode = null);

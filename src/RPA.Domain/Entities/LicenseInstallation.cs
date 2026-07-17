namespace RPA.Domain.Entities;

public class LicenseInstallation : BaseEntity
{
    public string InstallationId { get; set; } = "";
    public string PublicKey { get; set; } = "";
    public string PublicKeyFingerprint { get; set; } = "";
    public string ProductId { get; set; } = "";
    public string? CustomerReference { get; set; }
    public DateTimeOffset InstallationCreatedAt { get; set; }
    public string? SignedLicenseDocument { get; set; }
    public int? InstalledLicenseRevision { get; set; }
}

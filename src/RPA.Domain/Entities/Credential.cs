namespace RPA.Domain.Entities;

public class Credential : BaseEntity
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = ""; // SAP, Web, API, Email, TOTP
    public string VaultKeyReference { get; set; } = ""; // Vault'ta saklanmış key
    public Guid EnvironmentId { get; set; }
    public Environment Environment { get; set; } = null!;
}

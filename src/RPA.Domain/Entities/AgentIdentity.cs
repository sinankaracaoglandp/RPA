using RPA.Domain.Enums;

namespace RPA.Domain.Entities;

public class AgentIdentity : BaseEntity
{
    public Guid LicenseInstallationId { get; set; }
    public string Name { get; set; } = "";
    public string? MachineFingerprint { get; set; }
    public AgentIdentityStatus Status { get; set; } = AgentIdentityStatus.PendingActivation;
    public string? CredentialHash { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? DisabledAt { get; set; }
    public DateTimeOffset? DeactivatedAt { get; set; }
}

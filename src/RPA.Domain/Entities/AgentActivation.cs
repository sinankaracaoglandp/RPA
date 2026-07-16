namespace RPA.Domain.Entities;

public class AgentActivation : BaseEntity
{
    public Guid AgentIdentityId { get; set; }
    public string ActivationCodeHash { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
}

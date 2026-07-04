namespace RPA.Domain.Entities;

public class AuditLog : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Action { get; set; } = ""; // create, edit, publish, delete, run, approve
    public string ResourceType { get; set; } = ""; // workflow, component, robot, queue, credential
    public Guid ResourceId { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
}

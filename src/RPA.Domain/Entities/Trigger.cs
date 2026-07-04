using RPA.Domain.Enums;

namespace RPA.Domain.Entities;

public class Trigger : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }
    public Guid WorkflowVersionId { get; set; }
    public TriggerType Type { get; set; }
    public string Configuration { get; set; } = "{}"; // JSON: cron, webhook URL, etc.
    public Guid EnvironmentId { get; set; }
    public bool IsActive { get; set; } = true;
}

using RPA.Domain.Enums;

namespace RPA.Domain.Entities;

public class WorkflowVersion : BaseEntity
{
    public Guid WorkflowId { get; set; }
    public Workflow Workflow { get; set; } = null!;
    public string Version { get; set; } = "1.0.0"; // SemVer
    public string JsonDefinition { get; set; } = "{}";
    public ComponentStatus Status { get; set; } = ComponentStatus.Draft;
    public string? ChangeNotes { get; set; }
    public Guid EnvironmentId { get; set; }
    public Environment Environment { get; set; } = null!;
}

namespace RPA.Domain.Entities;

public class ComponentUsage : BaseEntity
{
    public Guid WorkflowVersionId { get; set; }
    public WorkflowVersion WorkflowVersion { get; set; } = null!;
    public Guid ComponentVersionId { get; set; }
    public ComponentVersion ComponentVersion { get; set; } = null!;
}

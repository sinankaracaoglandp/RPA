namespace RPA.Domain.Entities;

public class Workflow : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Tags { get; set; }
    public Guid? ActiveVersionId { get; set; }
    public WorkflowVersion? ActiveVersion { get; set; }
    public ICollection<WorkflowVersion> Versions { get; } = new List<WorkflowVersion>();
    public ICollection<ComponentUsage> ComponentUsages { get; } = new List<ComponentUsage>();
    public ICollection<JobRun> JobRuns { get; } = new List<JobRun>();
}

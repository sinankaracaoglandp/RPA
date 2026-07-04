namespace RPA.Domain.Entities;

public class JobRun : BaseEntity
{
    public Guid WorkflowVersionId { get; set; }
    public WorkflowVersion WorkflowVersion { get; set; } = null!;
    public string TriggeredBy { get; set; } = ""; // manual, cron, api, email, queue
    public Guid? AssignedRobotId { get; set; }
    public Guid EnvironmentId { get; set; }
    public string Status { get; set; } = "Running"; // Running, Successful, Failed, BusinessException, Abandoned
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string ElasticsearchCorrelationId { get; set; } = ""; // Korelasyon ID
    public string? ScreenshotArchivePath { get; set; }
}

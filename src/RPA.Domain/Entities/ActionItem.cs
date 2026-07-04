namespace RPA.Domain.Entities;

public class ActionItem : BaseEntity
{
    public string Type { get; set; } = ""; // BusinessException, OtpRequest, Approval
    public Guid? JobRunId { get; set; }
    public Guid? QueueItemId { get; set; }
    public Guid? AssignedUserId { get; set; }
    public Guid? AssignedRoleId { get; set; }
    public string Status { get; set; } = "Pending"; // Pending, Resolved, Timedout
    public string? ResolutionNote { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public DateTime? TimeoutAt { get; set; }
}

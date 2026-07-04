using RPA.Domain.Enums;

namespace RPA.Domain.Entities;

public class QueueItem : BaseEntity
{
    public Guid QueueId { get; set; }
    public Queue Queue { get; set; } = null!;
    public string IdempotencyKey { get; set; } = "";
    public string Payload { get; set; } = "{}";
    public QueueItemStatus Status { get; set; } = QueueItemStatus.New;
    public int AttemptCount { get; set; }
    public Guid? AssignedRobotId { get; set; }
    public Robot? AssignedRobot { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorDetail { get; set; }
    public string? CheckpointData { get; set; }
}

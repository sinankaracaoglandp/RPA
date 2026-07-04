using RPA.Domain.Enums;

namespace RPA.Domain.Entities;

public class Robot : BaseEntity
{
    public string MachineName { get; set; } = "";
    public RobotMode Mode { get; set; }
    public string Tags { get; set; } = "";
    public RobotStatus Status { get; set; } = RobotStatus.Offline;
    public DateTime? LastHeartbeat { get; set; }
    public string? AgentVersion { get; set; }
    public int Capacity { get; set; } = 1;
    public ICollection<QueueItem> QueueItems { get; } = new List<QueueItem>();
}

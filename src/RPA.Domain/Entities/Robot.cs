using RPA.Domain.Enums;

namespace RPA.Domain.Entities;

public class Robot : BaseEntity
{
    public string MachineName { get; set; } = "";

    /// <summary>
    /// Bu robotu kaydeden lisansli ajan kimligi (sahiplik bagi). Register sirasinda ajanin
    /// JWT'sindeki agent_id'den yazilir — istemciden GELMEZ. Heartbeat/register cagrilarinda
    /// caginin bu robotun sahibi oldugu bununla dogrulanir; aksi halde kimligi dogrulanmis
    /// herhangi bir ajan baska bir robotun kimligine burunup ona atanan isleri alabilirdi.
    /// Null = ajansiz/eski kayit (or. dogrudan RobotService ile olusturulmus test kaydi).
    /// </summary>
    public Guid? AgentIdentityId { get; set; }
    public RobotMode Mode { get; set; }
    public string Tags { get; set; } = "";
    public RobotStatus Status { get; set; } = RobotStatus.Offline;
    public DateTime? LastHeartbeat { get; set; }
    public string? AgentVersion { get; set; }
    public int Capacity { get; set; } = 1;
    public ICollection<QueueItem> QueueItems { get; } = new List<QueueItem>();
}

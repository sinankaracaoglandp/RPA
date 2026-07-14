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

    /// <summary>Bu job'un hangi robot havuzunda koşacağı — virgülle ayrık etiketler
    /// (örn. "prod-vm,sap"). Boşsa etiket kısıtı yok. Robot.Tags bunları kapsamalı.</summary>
    public string TargetRobotTags { get; set; } = "";

    /// <summary>Eşit uygunlukta adaylar arasında sıralama önceliği (büyük = önce).</summary>
    public int Priority { get; set; } = 0;
}

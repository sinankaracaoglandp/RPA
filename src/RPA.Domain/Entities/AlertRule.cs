namespace RPA.Domain.Entities;

public class AlertRule : BaseEntity
{
    public string Name { get; set; } = "";
    public string Condition { get; set; } = ""; // JSON: SystemException count, Business exception count, robot offline, SLA breach
    public string Channel { get; set; } = ""; // email, teams
    public string Recipients { get; set; } = ""; // comma-separated emails/webhook URLs
    public bool IsActive { get; set; } = true;
}

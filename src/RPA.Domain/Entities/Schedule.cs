namespace RPA.Domain.Entities;

public class Schedule : BaseEntity
{
    public Guid TriggerId { get; set; }
    public string CronExpression { get; set; } = "";
    public string TimeZone { get; set; } = "UTC";
    public string OverlapPolicy { get; set; } = "skip"; // skip, queue, parallel
}

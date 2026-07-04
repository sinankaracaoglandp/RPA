namespace RPA.Domain.Entities;

public class Queue : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public string Name { get; set; } = "";
    public int MaxRetries { get; set; } = 3;
    public string RetryBackoffPolicy { get; set; } = "exponential";
    public int? SlaSeconds { get; set; }
    public bool RequireIdempotency { get; set; } = true;
    public ICollection<QueueItem> Items { get; } = new List<QueueItem>();
}

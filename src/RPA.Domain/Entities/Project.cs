namespace RPA.Domain.Entities;

public class Project : BaseEntity
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public ICollection<Workflow> Workflows { get; } = new List<Workflow>();
    public ICollection<Component> Components { get; } = new List<Component>();
    public ICollection<Queue> Queues { get; } = new List<Queue>();
    public ICollection<EInvoiceProfile> EInvoiceProfiles { get; } = new List<EInvoiceProfile>();
}

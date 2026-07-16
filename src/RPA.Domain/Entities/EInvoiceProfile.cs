namespace RPA.Domain.Entities;

public sealed class EInvoiceProfile : BaseEntity
{
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string DraftDefinitionJson { get; set; } = "{\"fields\":[],\"collections\":[]}";
    public Project? Project { get; set; }
    public ICollection<EInvoiceProfileVersion> Versions { get; } = new List<EInvoiceProfileVersion>();
}

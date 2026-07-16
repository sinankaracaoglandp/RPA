namespace RPA.Domain.Entities;

public sealed class EInvoiceProfileVersion : BaseEntity
{
    public Guid ProfileId { get; set; }
    public int Version { get; set; }
    public string DefinitionJson { get; set; } = string.Empty;
    public string OutputSchemaJson { get; set; } = string.Empty;
    public DateTime PublishedAt { get; set; }
    public Guid? PublishedBy { get; set; }
    public EInvoiceProfile? Profile { get; set; }
}

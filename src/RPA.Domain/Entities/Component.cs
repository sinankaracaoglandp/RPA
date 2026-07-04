namespace RPA.Domain.Entities;

public class Component : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Tags { get; set; }
    public string OwnerAdUsername { get; set; } = "";
    public ICollection<ComponentVersion> Versions { get; } = new List<ComponentVersion>();
}

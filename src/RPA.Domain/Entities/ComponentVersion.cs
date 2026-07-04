using RPA.Domain.Enums;

namespace RPA.Domain.Entities;

public class ComponentVersion : BaseEntity
{
    public Guid ComponentId { get; set; }
    public Component Component { get; set; } = null!;
    public string Version { get; set; } = "1.0.0"; // SemVer
    public string JsonDefinition { get; set; } = "{}";
    public string InputOutputSchema { get; set; } = "{}";
    public ComponentStatus Status { get; set; } = ComponentStatus.Draft;
}

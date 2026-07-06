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

    // Kütüphane görüntüleme metaverisi (opsiyonel — Faz 5 Task 5.3 Component Library UI).
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Author { get; set; }
    public string? Category { get; set; }
}

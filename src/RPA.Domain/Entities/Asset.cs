namespace RPA.Domain.Entities;

public class Asset : BaseEntity
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = ""; // text, number, bool, json
    public string? Value { get; set; }
    public Guid EnvironmentId { get; set; }
    public Environment Environment { get; set; } = null!;
}

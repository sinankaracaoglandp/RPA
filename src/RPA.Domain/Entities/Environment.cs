namespace RPA.Domain.Entities;

public class Environment : BaseEntity
{
    public string Name { get; set; } = ""; // Dev, Test, Prod
    public string Description { get; set; } = "";
    public ICollection<Credential> Credentials { get; } = new List<Credential>();
    public ICollection<Asset> Assets { get; } = new List<Asset>();
}

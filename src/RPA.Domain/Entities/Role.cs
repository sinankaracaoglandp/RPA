namespace RPA.Domain.Entities;

public class Role : BaseEntity
{
    public string Name { get; set; } = ""; // Geliştirici, Onaylayan, İzleyici, Yönetici, Operatör
    public ICollection<UserRole> Users { get; } = new List<UserRole>();
    public ICollection<Permission> Permissions { get; } = new List<Permission>();
}

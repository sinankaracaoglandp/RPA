namespace RPA.Domain.Entities;

public class User : BaseEntity
{
    public string AdUsername { get; set; } = ""; // unique
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public ICollection<UserRole> Roles { get; } = new List<UserRole>();
    public ICollection<AuditLog> AuditLogs { get; } = new List<AuditLog>();
}

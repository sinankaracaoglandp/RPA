namespace RPA.Domain.Entities;

public class Permission : BaseEntity
{
    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;
    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }
    public string Action { get; set; } = ""; // view, edit, publish, run, approve
    public string Resource { get; set; } = ""; // workflow, component, robot, queue, credential
}

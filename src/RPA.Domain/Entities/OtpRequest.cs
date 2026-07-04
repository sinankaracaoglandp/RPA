using RPA.Domain.Enums;

namespace RPA.Domain.Entities;

public class OtpRequest : BaseEntity
{
    public Guid JobRunId { get; set; }
    public OtpChannel Channel { get; set; }
    public string PortalReference { get; set; } = "";
    public string EncryptedCode { get; set; } = "";
    public string Status { get; set; } = "Pending"; // Pending, Verified, Timedout
    public DateTime? VerifiedAt { get; set; }
    public DateTime TimeoutAt { get; set; }
}

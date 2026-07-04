namespace RPA.Infrastructure.Audit;

/// <summary>
/// AuditLog feature toggle ayarları (appsettings.json "AuditLog" section).
/// </summary>
public class AuditOptions
{
    public const string SectionName = "AuditLog";

    /// <summary>
    /// false ise AuditInterceptor entity değişikliklerini yakalamaz.
    /// Varsayılan: açık.
    /// </summary>
    public bool Enabled { get; set; } = true;
}

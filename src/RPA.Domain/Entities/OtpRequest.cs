using RPA.Domain.Enums;

namespace RPA.Domain.Entities;

/// <summary>
/// Bir portal girişi için istenen OTP/2FA kodunun kalıcı (audit) kaydı.
/// Kod daima <see cref="EncryptedCode"/> alanında AES ile şifreli tutulur — plaintext saklanmaz.
/// Spec Bölüm 7.
/// </summary>
public class OtpRequest : BaseEntity
{
    /// <summary>İlgili iş çalışması (korelasyon / audit).</summary>
    public Guid JobRunId { get; set; }

    /// <summary>Kodun alındığı kanal.</summary>
    public OtpChannel Channel { get; set; }

    /// <summary>Portal URL'i / kimliği.</summary>
    public string PortalReference { get; set; } = "";

    /// <summary>AES ile şifrelenmiş OTP kodu (plaintext asla saklanmaz).</summary>
    public string EncryptedCode { get; set; } = "";

    /// <summary>Durum (Pending, Provided, Expired, Failed).</summary>
    public OtpRequestStatus Status { get; set; } = OtpRequestStatus.Pending;

    /// <summary>Kodun geçerlilik sonu (varsayılan TTL 5 dk).</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Kodun sağlandığı an (başarılıysa).</summary>
    public DateTime? ProvidedAt { get; set; }
}

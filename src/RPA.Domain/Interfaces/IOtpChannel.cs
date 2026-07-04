namespace RPA.Domain.Interfaces;

using RPA.Domain.Enums;

/// <summary>
/// OTP/2FA kod alma — 5 kanal.
/// Spec Bölüm 7: e-posta, TOTP, GSM modem, telefon yönlendirme, insan onaylı.
/// Her kanal bu arayüzü implements eder.
/// </summary>
public interface IOtpChannel
{
    /// <summary>
    /// Hangi kanal? (Email, Totp, GsmModem, PhoneForward, HumanApproval)
    /// </summary>
    OtpChannel ChannelType { get; }

    /// <summary>
    /// Kod al.
    /// </summary>
    /// <param name="request">Portal referansı, timeout, VS.</param>
    /// <param name="cancellationToken">İptal sinyali (timeout öncesi iptal mümkün)</param>
    /// <returns>Doğru kod veya timeout exception</returns>
    /// <exception cref="TimeoutException">Timeout — fallback kanalına geç</exception>
    /// <exception cref="BusinessException">Kanal başarısız (fallback gerekli)</exception>
    Task<string> GetCodeAsync(OtpRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Kanal ayağa mı (health check)?
    /// </summary>
    Task<bool> IsHealthyAsync();
}

/// <summary>
/// GetCodeAsync için request parametreleri.
/// </summary>
public class OtpRequest
{
    /// <summary>
    /// Portal URL'i veya identity (örn. "https://portal.example.com/app")
    /// </summary>
    public required string PortalReference { get; set; }

    /// <summary>
    /// Gelen kodun expected formatı veya pattern (örn. "\\d{6}" = 6-digit code).
    /// </summary>
    public string? CodePattern { get; set; } = "\\d{6}";

    /// <summary>
    /// Kaç saniye bekleyelim? (fallback trigger zamanı)
    /// </summary>
    public int TimeoutSeconds { get; set; } = 180; // 3 dakika varsayılan

    /// <summary>
    /// E-posta kanalı için: gelen kutusu hesabı
    /// </summary>
    public string? EmailAccount { get; set; }

    /// <summary>
    /// TOTP kanalı için: secret vault key referansı
    /// </summary>
    public string? TotpSecretKey { get; set; }

    /// <summary>
    /// GSM modem kanalı için: beklenen SMS gönderici numarası
    /// </summary>
    public string? GsmSenderNumber { get; set; }

    /// <summary>
    /// Telefon yönlendirme kanalı için: webhook URL'i
    /// </summary>
    public string? WebhookUrl { get; set; }

    /// <summary>
    /// İnsan onaylı kanalı için: atanan kullanıcı/rol
    /// </summary>
    public Guid? AssignedUserId { get; set; }
    public Guid? AssignedRoleId { get; set; }
}

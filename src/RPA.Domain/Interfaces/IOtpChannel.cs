namespace RPA.Domain.Interfaces;

using RPA.Domain.Entities;
using RPA.Domain.Enums;

/// <summary>
/// OTP/2FA kod alma — tek bir kanal sözleşmesi (Spec Bölüm 7).
/// 5 implementasyon: Email (IMAP/EWS), TOTP (Otp.NET), GSM modem (AT komut),
/// telefon yönlendirme (webhook), insan onaylı (Action Center).
/// <see cref="OtpChannelFactory"/> tür → implementasyon eşler;
/// <c>GetOtpActivity</c> kanalları sırayla dener (timeout → sonraki kanal).
/// </summary>
public interface IOtpChannel
{
    /// <summary>Bu implementasyonun temsil ettiği kanal türü.</summary>
    OtpChannel ChannelType { get; }

    /// <summary>
    /// OTP kodunu bu kanaldan alır. Verilen <paramref name="timeout"/> içinde kod
    /// gelmezse <see cref="System.TimeoutException"/> fırlatır (fallback tetiklenir).
    /// </summary>
    /// <param name="request">İlgili OTP isteği (entity — portal referansı, JobRun vb.).</param>
    /// <param name="timeout">Bu kanal için maksimum bekleme süresi.</param>
    /// <param name="cancellationToken">İptal token'ı.</param>
    /// <returns>Plaintext OTP kodu (çağıran şifreler ve maskeler).</returns>
    /// <exception cref="System.TimeoutException">Kod zamanında gelmezse.</exception>
    Task<string> GetOtpAsync(OtpRequest request, TimeSpan timeout, CancellationToken cancellationToken);
}

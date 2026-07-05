namespace RPA.Infrastructure.OTP;

using System.Threading;
using System.Threading.Tasks;
using RPA.Domain.Entities;
using RPA.Domain.Interfaces;

/// <summary>
/// OTP kodunun kalıcı depolama öncesi şifrelenmesi ve loglarda maskelenmesi (Spec Bölüm 7).
/// Plaintext kod hiçbir zaman DB'ye yazılmaz, loglarda "[MASKED]" gösterilir.
/// </summary>
public interface IOtpCodeProtector
{
    /// <summary>Kodu AES ile şifrele (kalıcı saklama için).</summary>
    string Encrypt(string plaintextCode);

    /// <summary>Şifreli kodu çöz (portal'a yazmak için, çalışma zamanı).</summary>
    string Decrypt(string cipherText);

    /// <summary>Kodu loglar için maskele — daima "[MASKED]".</summary>
    string Mask(string code);
}

/// <summary>
/// Her OtpRequest'in audit trail'e yazılması (Spec Bölüm 7: "Audit: her OtpRequest loglanır").
/// Infrastructure/Persistence tarafından implemente edilir; birim testlerde mock'lanır.
/// </summary>
public interface IOtpAuditSink
{
    Task RecordAsync(OtpRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// E-posta (IMAP/EWS) gelen kutusu OTP okuyucusu. Gerçek MailKit/EWS implementasyonu
/// entegrasyon kapsamındadır; birim testlerde fixture ile mock'lanır.
/// </summary>
public interface IImapOtpReader
{
    /// <summary>
    /// Gönderici + konu filtresine uyan en son mesajın gövdesini döndürür (okundu işaretler).
    /// Timeout içinde uygun mesaj gelmezse <c>null</c> döner.
    /// </summary>
    Task<string?> FetchLatestMessageBodyAsync(
        string? emailAccount,
        string? senderFilter,
        string? subjectFilter,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// USB GSM modem (AT komutları) SMS okuyucusu. Gerçek seri port implementasyonu
/// entegrasyon kapsamındadır; birim testlerde fixture ile mock'lanır.
/// </summary>
public interface IGsmModemDevice
{
    /// <summary>
    /// Beklenen gönderici numarasından gelen en son SMS metnini döndürür.
    /// Timeout içinde SMS gelmezse <c>null</c> döner.
    /// </summary>
    Task<string?> ReadLatestSmsAsync(
        string? senderNumber,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Telefon yönlendirme webhook dinleyicisi. Gerçek HTTP endpoint entegrasyon kapsamındadır;
/// birim testlerde fixture ile mock'lanır.
/// </summary>
public interface IWebhookOtpListener
{
    /// <summary>
    /// Verilen referans için POST edilen yönlendirilmiş mesaj gövdesini bekler.
    /// Timeout içinde payload gelmezse <c>null</c> döner.
    /// </summary>
    Task<string?> WaitForForwardedMessageAsync(
        string reference,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Action Center üzerinden insan onaylı OTP isteği. Attended modda dialog,
/// unattended modda ActionItem + bildirim. Birim testlerde mock'lanır.
/// </summary>
public interface IActionCenterClient
{
    /// <summary>
    /// İnsandan OTP kodunu ister; kullanıcı yanıtını (kod) döndürür.
    /// Timeout içinde yanıt gelmezse <c>null</c> döner.
    /// </summary>
    Task<string?> RequestOtpFromHumanAsync(
        OtpRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

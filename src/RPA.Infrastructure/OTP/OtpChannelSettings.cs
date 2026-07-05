namespace RPA.Infrastructure.OTP;

using System.Text.RegularExpressions;

/// <summary>
/// Kanal bağımsız yapılandırma (Spec Bölüm 7). DI ile <c>IOptions</c> üzerinden veya
/// <c>GetOtpActivity</c> tarafından context değişkenlerinden doldurulur.
/// OTP entity'si audit odaklıdır; kanala özgü teknik parametreler burada taşınır.
/// </summary>
public sealed class OtpChannelSettings
{
    /// <summary>Beklenen kod deseni (varsayılan 6 haneli).</summary>
    public string CodePattern { get; set; } = @"\d{6}";

    // Email
    public string? EmailAccount { get; set; }
    public string? EmailSenderFilter { get; set; }
    public string? EmailSubjectFilter { get; set; }

    // TOTP
    /// <summary>Base32 TOTP secret (vault'tan çözülmüş — plaintext olarak loglanmaz).</summary>
    public string? TotpSecret { get; set; }

    // GSM modem
    public string? GsmSenderNumber { get; set; }

    // Phone forward (webhook)
    public string? WebhookReference { get; set; }
}

/// <summary>
/// Serbest metin (e-posta gövdesi, SMS) içinden OTP kodunu regex ile ayıklar (Spec Bölüm 7).
/// </summary>
public static class OtpCodeExtractor
{
    /// <summary>
    /// <paramref name="body"/> içinde <paramref name="pattern"/> ile ilk eşleşmeyi döndürür.
    /// Eşleşme yoksa <c>null</c>.
    /// </summary>
    public static string? Extract(string? body, string pattern)
    {
        if (string.IsNullOrEmpty(body))
        {
            return null;
        }

        var effective = string.IsNullOrWhiteSpace(pattern) ? @"\d{6}" : pattern;
        var match = Regex.Match(body, effective);
        return match.Success ? match.Value : null;
    }
}

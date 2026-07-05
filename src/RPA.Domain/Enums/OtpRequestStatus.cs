namespace RPA.Domain.Enums;

/// <summary>
/// OtpRequest yaşam döngüsü durumları (Spec Bölüm 7).
/// </summary>
public enum OtpRequestStatus
{
    /// <summary>Kod bekleniyor (kanal denemesi sürüyor).</summary>
    Pending,

    /// <summary>Kod başarıyla alındı (şifreli saklandı).</summary>
    Provided,

    /// <summary>TTL doldu / kod zamanında gelmedi.</summary>
    Expired,

    /// <summary>Tüm kanallar başarısız oldu.</summary>
    Failed
}

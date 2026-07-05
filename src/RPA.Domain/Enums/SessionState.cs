namespace RPA.Domain.Enums;

/// <summary>
/// Windows oturum durumu. Active kullanıcının etkileşimli çalıştığı konsol oturumu;
/// Disconnected oturum açık fakat bağlı değil (RDP kopması); LoggedOff oturum kapalı;
/// Locked ekran kilitli. Unknown durum tespit edilemedi.
/// </summary>
public enum SessionState
{
    Unknown,
    Active,
    Disconnected,
    LoggedOff,
    Locked
}

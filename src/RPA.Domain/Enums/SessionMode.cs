namespace RPA.Domain.Enums;

/// <summary>
/// Robot Windows oturum modu (Spec Bölüm 9). Attended kullanıcı tarafından başlatılan
/// interaktif oturum; Unattended başlangıçta AutoLogon ile açılan servis oturumu;
/// Rdp Orchestrator'dan uzak masaüstü ile bağlanılan oturum.
/// </summary>
public enum SessionMode
{
    Attended,
    Unattended,
    Rdp
}

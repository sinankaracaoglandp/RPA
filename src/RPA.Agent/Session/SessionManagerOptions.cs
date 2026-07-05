namespace RPA.Agent.Session;

/// <summary>
/// Oturum yöneticisi yapılandırması. appsettings.json'daki "Agent:Session" bölümünden okunur.
/// AutoLogon güvenlik açısından hassastır: yalnızca development/test ortamında etkinleştirilmelidir
/// (<see cref="AllowAutoLogon"/>). Parola asla burada tutulmaz — Vault'tan
/// <see cref="AutoLogonPasswordVaultKey"/> anahtarıyla okunur.
/// </summary>
public sealed class SessionManagerOptions
{
    public const string SectionName = "Agent:Session";

    /// <summary>
    /// AutoLogon kurulumuna izin verilir mi? Güvenlik gereği yalnızca dev/test ortamı.
    /// Üretimde false olmalı; false iken Unattended EnsureSession denemesi hata verir.
    /// </summary>
    public bool AllowAutoLogon { get; set; }

    /// <summary>AutoLogon kullanıcı adı (gizli değil). Örn. "rpa-robot".</summary>
    public string AutoLogonUserName { get; set; } = "";

    /// <summary>AutoLogon alan adı (opsiyonel). Boşsa yerel makine hesabı.</summary>
    public string AutoLogonDomain { get; set; } = "";

    /// <summary>Parolanın Vault'ta saklandığı anahtar. Örn. "autologon-robot-password".</summary>
    public string AutoLogonPasswordVaultKey { get; set; } = "";
}

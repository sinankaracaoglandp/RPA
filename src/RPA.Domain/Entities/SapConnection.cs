namespace RPA.Domain.Entities;

using RPA.Domain.Enums;

/// <summary>
/// Bir SAP sistemine bağlantı tanımı (Spec Bölüm 6). Sistem/client/dil bilgisi tutulur;
/// kimlik bilgileri asla plaintext saklanmaz — yalnızca Credential Vault key referansı
/// (<see cref="CredentialVaultKeyRef"/>) tutulur (CLAUDE.md Kısım 3).
/// </summary>
public class SapConnection : BaseEntity
{
    /// <summary>Sistem adı / System ID (örn. "DEV", "PRD").</summary>
    public string SystemName { get; set; } = "";

    /// <summary>SAP client (mandant) (örn. "100").</summary>
    public string Client { get; set; } = "";

    /// <summary>Oturum açma dili (örn. "TR", "EN").</summary>
    public string LanguageCode { get; set; } = "EN";

    /// <summary>Credential Vault key referansı — kullanıcı/şifre buradan çözülür (plaintext saklanmaz).</summary>
    public string CredentialVaultKeyRef { get; set; } = "";

    /// <summary>Kanal türü — GUI Scripting veya NCo.</summary>
    public SapChannelType ConnectionType { get; set; } = SapChannelType.Gui;

    /// <summary>Bağlantı/işlem zaman aşımı (saniye). Varsayılan 60 sn.</summary>
    public int Timeout { get; set; } = 60;

    /// <summary>Bağlantı aktif mi?</summary>
    public bool IsActive { get; set; } = true;
}

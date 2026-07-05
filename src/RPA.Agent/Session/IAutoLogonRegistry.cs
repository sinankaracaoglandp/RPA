namespace RPA.Agent.Session;

/// <summary>
/// Winlogon AutoLogon registry anahtarlarını yönetir
/// (HKLM\Software\Microsoft\Windows NT\CurrentVersion\Winlogon).
/// P/Invoke/registry erişimini soyutlar; böylece <see cref="WindowsSessionManager"/>
/// birim testlerinde gerçek registry'ye dokunmadan doğrulanabilir.
/// </summary>
public interface IAutoLogonRegistry
{
    /// <summary>
    /// AutoLogon anahtarlarını yazar: DefaultUserName, DefaultDomainName,
    /// DefaultPassword, AutoAdminLogon=1. Parola yalnızca yazma anında iletilir,
    /// loglanmaz. Not: DefaultPassword'un registry'de plaintext olması Windows
    /// AutoLogon mekanizmasının gereğidir; bu yüzden yalnızca dev/test için desteklenir.
    /// </summary>
    void Configure(string userName, string? domain, string password);

    /// <summary>AutoLogon anahtarlarını temizler (AutoAdminLogon=0, parola siler).</summary>
    void Clear();
}

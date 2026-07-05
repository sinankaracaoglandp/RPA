namespace RPA.Agent.Session;

using System.Runtime.Versioning;
using Microsoft.Win32;

/// <summary>
/// <see cref="IAutoLogonRegistry"/> Windows implementasyonu. Winlogon anahtarlarını yazar:
/// HKLM\Software\Microsoft\Windows NT\CurrentVersion\Winlogon.
///
/// GÜVENLİK: Windows AutoLogon mekanizması DefaultPassword'u registry'de plaintext bekler;
/// bu yüzden AutoLogon yalnızca dev/test ortamında (AllowAutoLogon) desteklenir. Parola
/// bu sınıfa yalnızca yazma anında iletilir, saklanmaz, loglanmaz.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WinlogonAutoLogonRegistry : IAutoLogonRegistry
{
    private const string WinlogonPath = @"Software\Microsoft\Windows NT\CurrentVersion\Winlogon";

    public void Configure(string userName, string? domain, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentNullException.ThrowIfNull(password);

        using var key = Registry.LocalMachine.OpenSubKey(WinlogonPath, writable: true)
            ?? throw new InvalidOperationException($"Winlogon registry anahtarı açılamadı: {WinlogonPath}");

        key.SetValue("DefaultUserName", userName, RegistryValueKind.String);
        key.SetValue("DefaultDomainName", domain ?? Environment.MachineName, RegistryValueKind.String);
        key.SetValue("DefaultPassword", password, RegistryValueKind.String);
        key.SetValue("AutoAdminLogon", "1", RegistryValueKind.String);
    }

    public void Clear()
    {
        using var key = Registry.LocalMachine.OpenSubKey(WinlogonPath, writable: true);
        if (key is null)
        {
            return;
        }

        key.SetValue("AutoAdminLogon", "0", RegistryValueKind.String);
        DeleteValueIfExists(key, "DefaultPassword");
    }

    private static void DeleteValueIfExists(RegistryKey key, string name)
    {
        if (key.GetValue(name) is not null)
        {
            key.DeleteValue(name, throwOnMissingValue: false);
        }
    }
}

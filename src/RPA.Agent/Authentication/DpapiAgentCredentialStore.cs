namespace RPA.Agent.Authentication;

using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using RPA.Agent.Configuration;

/// <summary>
/// <see cref="IAgentCredentialStore"/>'un Windows DPAPI (LocalMachine kapsami) implementasyonu.
/// Credential ProgramData altindaki bir dosyaya sifreli olarak yazilir; makine disina tasinamaz.
/// Ek entropi olarak sabit bir uygulama etiketi kullanilir.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiAgentCredentialStore : IAgentCredentialStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("RPA.Agent.Credential.v1");

    private readonly string _filePath;

    public DpapiAgentCredentialStore(IOptions<AgentOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _filePath = options.Value.EffectiveCredentialFilePath;
    }

    public string? TryGetCredential()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            var protectedBytes = File.ReadAllBytes(_filePath);
            var plain = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.LocalMachine);
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException)
        {
            // Baska makinede/kapsamda korunmus veya bozulmus dosya — credential yok sayilir.
            return null;
        }
    }

    public void SaveCredential(string credential)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credential);

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(credential), Entropy, DataProtectionScope.LocalMachine);
        File.WriteAllBytes(_filePath, protectedBytes);
    }

    public void Clear()
    {
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }
    }
}

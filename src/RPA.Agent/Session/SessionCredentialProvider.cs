namespace RPA.Agent.Session;

using Microsoft.Extensions.Options;
using RPA.Domain.Interfaces;

/// <summary>
/// AutoLogon kimlik bilgilerini üretir. Kullanıcı adı/alan yapılandırmadan (gizli değil),
/// parola ise <see cref="ICredentialVault"/>'tan okunur — asla registry/config/log'da
/// plaintext tutulmaz. Parola yalnızca <see cref="AutoLogonCredential.RevealPassword"/>
/// çağrısında (registry yazma anında) geçici olarak çözülür.
/// </summary>
public sealed class SessionCredentialProvider
{
    private readonly ICredentialVault _vault;
    private readonly SessionManagerOptions _options;

    public SessionCredentialProvider(ICredentialVault vault, IOptions<SessionManagerOptions> options)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>AutoLogon kimlik bilgisini Vault'tan hazırlar.</summary>
    /// <exception cref="InvalidOperationException">Kullanıcı adı veya Vault anahtarı boşsa.</exception>
    public async Task<AutoLogonCredential> GetAutoLogonCredentialAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.AutoLogonUserName))
        {
            throw new InvalidOperationException("AutoLogon kullanıcı adı yapılandırılmamış.");
        }

        if (string.IsNullOrWhiteSpace(_options.AutoLogonPasswordVaultKey))
        {
            throw new InvalidOperationException("AutoLogon parola Vault anahtarı yapılandırılmamış.");
        }

        var secret = await _vault.GetSecretAsync(_options.AutoLogonPasswordVaultKey).ConfigureAwait(false);
        var domain = string.IsNullOrWhiteSpace(_options.AutoLogonDomain) ? null : _options.AutoLogonDomain;
        return new AutoLogonCredential(_options.AutoLogonUserName, domain, secret);
    }
}

/// <summary>
/// AutoLogon kimlik bilgisi. Parola DPAPI ile şifreli <see cref="SecureString"/> içinde tutulur;
/// yalnızca <see cref="RevealPassword"/> ile (registry yazma anında) çözülür.
/// </summary>
public sealed class AutoLogonCredential
{
    private readonly SecureString _password;

    public AutoLogonCredential(string userName, string? domain, SecureString password)
    {
        UserName = userName ?? throw new ArgumentNullException(nameof(userName));
        Domain = domain;
        _password = password ?? throw new ArgumentNullException(nameof(password));
    }

    public string UserName { get; }

    public string? Domain { get; }

    /// <summary>Parolayı yalnızca gerektiğinde (registry yazma) çözer.</summary>
    public string RevealPassword() => _password.Decrypt();
}

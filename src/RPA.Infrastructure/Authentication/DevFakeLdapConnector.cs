namespace RPA.Infrastructure.Authentication;

using Microsoft.Extensions.Logging;

/// <summary>
/// Yalnızca geliştirme (Debug/Development) için sahte LDAP connector.
/// Gerçek Active Directory'ye <b>bind atmaz</b> — böylece hatalı parola denemeleri
/// domain hesabının kilitlenmesine yol açmaz. Boş olmayan her kullanıcı adı/parola
/// çiftini geçerli sayar ve geliştirici rollerini döner.
///
/// Bu tip production'da <b>asla</b> kaydedilmez (bkz.
/// <see cref="AuthenticationServiceCollectionExtensions.AddRpaAuthentication"/> —
/// yalnız <c>useFakeLdap</c> true iken devreye girer).
/// </summary>
public sealed class DevFakeLdapConnector : ILdapConnector
{
    // Geliştiricinin Studio'da tüm ekranlara erişebilmesi için geniş rol kümesi.
    private static readonly List<string> DevGroups = new()
    {
        "Administrator",
        "Developer",
    };

    private readonly ILogger<DevFakeLdapConnector> _logger;

    public DevFakeLdapConnector(ILogger<DevFakeLdapConnector> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<LdapAuthOutcome> AuthenticateAndGetGroupsAsync(string username, string password)
    {
        // NOT: Parola asla loglanmaz. Gerçek AD'ye bind atılmadığı için kilitlenme riski yoktur.
        _logger.LogWarning(
            "GELİŞTİRME SAHTE LDAP aktif — '{User}' gerçek AD doğrulaması olmadan kabul edildi. " +
            "Bu yalnızca Debug/Development içindir.", username);

        return Task.FromResult(new LdapAuthOutcome
        {
            Authenticated = true,
            Groups = new List<string>(DevGroups),
        });
    }
}

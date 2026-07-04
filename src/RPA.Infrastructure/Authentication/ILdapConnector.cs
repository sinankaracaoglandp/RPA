namespace RPA.Infrastructure.Authentication;

/// <summary>
/// LDAP bağlantısının test edilebilir soyutlaması. Gerçek implementasyon
/// (LdapForNet) AD sunucusuna bind eder; testlerde sahte connector kullanılır.
/// </summary>
public interface ILdapConnector
{
    /// <summary>
    /// Kullanıcı adı/şifre ile AD'ye bind eder ve grup üyeliklerini döner.
    /// </summary>
    Task<LdapAuthOutcome> AuthenticateAndGetGroupsAsync(string username, string password);
}

/// <summary>
/// LDAP bind + grup sorgu sonucu.
/// </summary>
public class LdapAuthOutcome
{
    public bool Authenticated { get; set; }

    /// <summary>Kullanıcının AD grup adları (CN). Rollere eşlenir.</summary>
    public List<string> Groups { get; set; } = new();

    public string? Error { get; set; }
}

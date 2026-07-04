namespace RPA.Infrastructure.Authentication;

/// <summary>
/// appsettings.json "Authentication" bölümü kök nesnesi.
/// </summary>
public class AuthenticationOptions
{
    public const string SectionName = "Authentication";

    public LdapOptions Ldap { get; set; } = new();
    public JwtOptions Jwt { get; set; } = new();
}

/// <summary>
/// On-premise Active Directory / LDAP bağlantı ayarları.
/// </summary>
public class LdapOptions
{
    /// <summary>ör. ldap://your-ad-server:389</summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>ör. dc=example,dc=com</summary>
    public string BaseDn { get; set; } = string.Empty;

    /// <summary>Kullanıcı arama filtresi; {0} sAMAccountName ile değiştirilir.</summary>
    public string SearchFilter { get; set; } = "(&(objectClass=user)(sAMAccountName={0}))";

    /// <summary>UPN/bind için domain (ör. EXAMPLE). Boşsa sadece kullanıcı adı ile bind denenir.</summary>
    public string Domain { get; set; } = string.Empty;
}

/// <summary>
/// JWT imzalama ve doğrulama ayarları.
/// </summary>
public class JwtOptions
{
    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "RPA.Platform";
    public string Audience { get; set; } = "RPA.Clients";
    public int ExpirationMinutes { get; set; } = 60;
}

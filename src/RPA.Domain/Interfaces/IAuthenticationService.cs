namespace RPA.Domain.Interfaces;

/// <summary>
/// On-premise AD/LDAP tabanlı kimlik doğrulama servisi sözleşmesi (Spec Bölüm 10).
/// AD kullanıcısını doğrular, grup üyeliğinden rol çıkarır ve JWT üretir.
/// </summary>
public interface IAuthenticationService
{
    /// <summary>
    /// AD kullanıcı adı + şifre ile kimlik doğrular.
    /// </summary>
    /// <param name="adUsername">Active Directory sAMAccountName</param>
    /// <param name="password">Kullanıcı şifresi (loglanmaz, saklanmaz)</param>
    /// <returns>Başarı durumu, JWT token ve roller</returns>
    Task<AuthenticationResult> AuthenticateAsync(string adUsername, string password);

    /// <summary>
    /// Geçerli refresh token ile yeni oturum belirteçleri üretir.
    /// </summary>
    Task<AuthenticationResult> RefreshAsync(string refreshToken);
}

/// <summary>
/// Kimlik doğrulama sonucu.
/// </summary>
public class AuthenticationResult
{
    public bool Success { get; set; }
    public string? AccessToken { get; set; }
    public string? JwtToken
    {
        get => AccessToken;
        set => AccessToken = value;
    }
    public string? RefreshToken { get; set; }
    public DateTime AccessTokenExpiresAtUtc { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> Roles { get; set; } = new();

    public static AuthenticationResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };

    public static AuthenticationResult Ok(string jwtToken, List<string> roles) =>
        new()
        {
            Success = true,
            AccessToken = jwtToken,
            RefreshToken = string.Empty,
            AccessTokenExpiresAtUtc = DateTime.UtcNow.AddMinutes(60),
            Roles = roles,
        };

    public static AuthenticationResult Ok(AuthTokenPair pair, List<string> roles) =>
        new()
        {
            Success = true,
            AccessToken = pair.AccessToken,
            RefreshToken = pair.RefreshToken,
            AccessTokenExpiresAtUtc = pair.AccessTokenExpiresAtUtc,
            Roles = roles,
        };
}

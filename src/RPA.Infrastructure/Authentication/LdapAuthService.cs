namespace RPA.Infrastructure.Authentication;

using Microsoft.Extensions.Logging;
using RPA.Domain.Interfaces;

/// <summary>
/// On-premise AD/LDAP kimlik doğrulama servisi (Spec Bölüm 10).
/// Kullanıcıyı AD'ye bind ederek doğrular, grup üyeliğinden rol çıkarır ve JWT üretir.
/// </summary>
public class LdapAuthService : IAuthenticationService
{
    private readonly ILdapConnector _connector;
    private readonly ITokenService _tokenService;
    private readonly ILogger<LdapAuthService> _logger;

    public LdapAuthService(
        ILdapConnector connector,
        ITokenService tokenService,
        ILogger<LdapAuthService> logger)
    {
        _connector = connector;
        _tokenService = tokenService;
        _logger = logger;
    }

    public async Task<AuthenticationResult> AuthenticateAsync(string adUsername, string password)
    {
        if (string.IsNullOrWhiteSpace(adUsername) || string.IsNullOrWhiteSpace(password))
        {
            return AuthenticationResult.Fail("Kullanıcı adı ve şifre zorunludur.");
        }

        LdapAuthOutcome outcome;
        try
        {
            // NOT: Şifre asla loglanmaz (Kontrat: credential maskeleme).
            outcome = await _connector.AuthenticateAndGetGroupsAsync(adUsername, password);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LDAP kimlik doğrulama hatası, kullanıcı {User}", adUsername);
            return AuthenticationResult.Fail("Kimlik doğrulama servisi hatası.");
        }

        if (!outcome.Authenticated)
        {
            _logger.LogWarning("Başarısız giriş denemesi, kullanıcı {User}", adUsername);
            return AuthenticationResult.Fail(outcome.Error ?? "Geçersiz kullanıcı adı veya şifre.");
        }

        var roles = MapGroupsToRoles(outcome.Groups);
        var pair = _tokenService.GenerateTokenPair(adUsername, roles);

        _logger.LogInformation(
            "Başarılı giriş, kullanıcı {User}, roller {Roles}", adUsername, string.Join(",", roles));

        return AuthenticationResult.Ok(pair, roles);
    }

    public Task<AuthenticationResult> RefreshAsync(string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return Task.FromResult(AuthenticationResult.Fail("Refresh token zorunludur."));
        }

        var validation = _tokenService.ValidateRefreshToken(refreshToken);
        if (!validation.Success || string.IsNullOrWhiteSpace(validation.Username))
        {
            return Task.FromResult(AuthenticationResult.Fail(validation.ErrorMessage ?? "Refresh token geçersiz."));
        }

        var roles = validation.Roles
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var pair = _tokenService.GenerateTokenPair(validation.Username, roles);

        _logger.LogInformation("Oturum yenilendi, kullanıcı {User}, roller {Roles}",
            validation.Username, string.Join(",", roles));

        return Task.FromResult(AuthenticationResult.Ok(pair, roles));
    }

    /// <summary>
    /// AD grup adlarını (CN) uygulama rollerine eşler. Varsayılan: grup CN'i rol adı olarak kullanılır.
    /// </summary>
    private static List<string> MapGroupsToRoles(IEnumerable<string> groups)
    {
        return groups
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Select(g => g.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

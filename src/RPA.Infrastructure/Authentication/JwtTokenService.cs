namespace RPA.Infrastructure.Authentication;

using System.Security.Cryptography;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RPA.Domain.Interfaces;

/// <summary>
/// Uygulama secret'ı ile HMAC-SHA256 imzalı JWT üretir (Spec Bölüm 10).
/// </summary>
public class JwtTokenService : ITokenService
{
    private const string TokenUseClaim = "token_use";
    private const string RefreshTokenUse = "refresh";
    private readonly JwtOptions _options;

    public JwtTokenService(IOptions<AuthenticationOptions> options)
    {
        _options = options.Value.Jwt;
    }

    public AuthTokenPair GenerateTokenPair(string username, IEnumerable<string> roles)
    {
        var roleList = roles
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var now = DateTime.UtcNow;
        var accessExpiresAt = now.AddMinutes(_options.ExpirationMinutes);

        return new AuthTokenPair
        {
            AccessToken = WriteToken(username, roleList, accessExpiresAt, tokenUse: "access"),
            RefreshToken = WriteToken(username, roleList, now.AddDays(_options.RefreshExpirationDays), tokenUse: RefreshTokenUse),
            AccessTokenExpiresAtUtc = accessExpiresAt,
        };
    }

    public string GenerateToken(string username, IEnumerable<string> roles)
    {
        var roleList = roles
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return WriteToken(
            username,
            roleList,
            DateTime.UtcNow.AddMinutes(_options.ExpirationMinutes),
            tokenUse: "access");
    }

    public RefreshTokenValidationResult ValidateRefreshToken(string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return RefreshTokenValidationResult.Fail("Refresh token boş olamaz.");
        }

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(refreshToken, BuildValidationParameters(), out var validatedToken);
            if (validatedToken is not JwtSecurityToken jwt)
            {
                return RefreshTokenValidationResult.Fail("Refresh token biçimi geçersiz.");
            }

            var tokenUse = principal.Claims.FirstOrDefault(c => c.Type == TokenUseClaim)?.Value;
            if (!string.Equals(tokenUse, RefreshTokenUse, StringComparison.Ordinal))
            {
                return RefreshTokenValidationResult.Fail("Refresh token türü geçersiz.");
            }

            var username = principal.Identity?.Name
                ?? principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(username))
            {
                return RefreshTokenValidationResult.Fail("Refresh token kullanıcı bilgisi içermiyor.");
            }

            var roles = principal.Claims
                .Where(c => c.Type == ClaimTypes.Role || c.Type == "role")
                .Select(c => c.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return RefreshTokenValidationResult.Ok(username, roles);
        }
        catch
        {
            return RefreshTokenValidationResult.Fail("Refresh token geçersiz veya süresi dolmuş.");
        }
    }

    private string WriteToken(string username, IEnumerable<string> roles, DateTime expiresAtUtc, string tokenUse)
    {
        var key = new SymmetricSecurityKey(DeriveSigningKey());
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, username),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(TokenUseClaim, tokenUse),
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAtUtc,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private TokenValidationParameters BuildValidationParameters() =>
        new()
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = _options.Issuer,
            ValidAudience = _options.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(DeriveSigningKey()),
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = JwtRegisteredClaimNames.Sub,
            RoleClaimType = ClaimTypes.Role,
        };

    private byte[] DeriveSigningKey() => JwtSigningKey.Derive(_options.Secret);
}

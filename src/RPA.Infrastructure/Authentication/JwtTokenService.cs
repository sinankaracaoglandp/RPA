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
    private readonly JwtOptions _options;

    public JwtTokenService(IOptions<AuthenticationOptions> options)
    {
        _options = options.Value.Jwt;
    }

    public string GenerateToken(string username, IEnumerable<string> roles)
    {
        if (string.IsNullOrWhiteSpace(_options.Secret) ||
            Encoding.UTF8.GetByteCount(_options.Secret) < 32)
        {
            throw new InvalidOperationException(
                "JWT secret yapılandırılmamış veya 32 byte'tan kısa.");
        }

        // HIGH FIX: Use PBKDF2 for proper key derivation instead of raw UTF-8 bytes.
        // This ensures low-entropy passphrases are strengthened via key derivation.
        var derivedKey = DeriveKeyFromSecret(_options.Secret);
        var key = new SymmetricSecurityKey(derivedKey);
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, username),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(_options.ExpirationMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Derives a cryptographically strong 32-byte key from the secret using PBKDF2.
    /// </summary>
    private static byte[] DeriveKeyFromSecret(string secret)
    {
        // Use PBKDF2 to derive a 32-byte key from the secret.
        // This strengthens the key against low-entropy input and provides proper key derivation.
        var secretBytes = Encoding.UTF8.GetBytes(secret);
        var saltBytes = Encoding.UTF8.GetBytes("RPA.JwtTokenService.v1"); // Fixed salt for consistency

        // Use static Pbkdf2 method (not deprecated constructor)
        return Rfc2898DeriveBytes.Pbkdf2(
            secretBytes,
            saltBytes,
            iterations: 10000,
            HashAlgorithmName.SHA256,
            outputLength: 32);
    }
}

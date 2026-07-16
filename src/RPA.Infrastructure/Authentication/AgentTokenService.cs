namespace RPA.Infrastructure.Authentication;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RPA.Domain.Entities;
using RPA.Domain.Enums;

public sealed class AgentTokenService
{
    private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(10);
    private readonly JwtOptions _options;

    public AgentTokenService(IOptions<AuthenticationOptions> options)
    {
        _options = options.Value.Jwt;
    }

    public string GenerateAccessToken(AgentIdentity agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        if (agent.Status != AgentIdentityStatus.Activated)
        {
            throw new UnauthorizedAccessException("AGENT_NOT_ACTIVE");
        }

        var now = DateTime.UtcNow;
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(DeriveSigningKey(_options.Secret)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims:
            [
                new Claim("agent_id", agent.Id.ToString()),
                new Claim("installation_id", agent.LicenseInstallationId.ToString()),
                new Claim("client_type", "agent"),
                new Claim("token_use", "access"),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            ],
            notBefore: now,
            expires: now.Add(AccessTokenLifetime),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static byte[] DeriveSigningKey(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret) || Encoding.UTF8.GetByteCount(secret) < 32)
        {
            throw new InvalidOperationException("JWT secret yapılandırılmamış veya 32 byte'tan kısa.");
        }

        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes("RPA.JwtTokenService.v1"),
            iterations: 10000,
            HashAlgorithmName.SHA256,
            outputLength: 32);
    }
}

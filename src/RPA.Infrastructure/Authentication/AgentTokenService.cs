namespace RPA.Infrastructure.Authentication;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
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
            new SymmetricSecurityKey(JwtSigningKey.Derive(_options.Secret)),
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
            ],
            notBefore: now,
            expires: now.Add(AccessTokenLifetime),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

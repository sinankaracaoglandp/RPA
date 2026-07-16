namespace RPA.WebAPI.Tests;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Authentication;

public class AgentAuthenticationTests
{
    [Fact]
    public void TokenExchange_CreatesTenMinuteAgentAccessTokenWithRequiredClaims()
    {
        var agentId = Guid.NewGuid();
        var installationId = Guid.NewGuid();
        var service = new AgentTokenService(Options.Create(new AuthenticationOptions
        {
            Jwt = new JwtOptions
            {
                Secret = "agent-token-service-test-secret-32bytes!!",
                Issuer = "RPA.Platform",
                Audience = "RPA.Clients",
                ExpirationMinutes = 60,
            },
        }));

        var token = service.GenerateAccessToken(new AgentIdentity
        {
            Id = agentId,
            LicenseInstallationId = installationId,
            Name = "agent-1",
            Status = AgentIdentityStatus.Activated,
        });

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal(agentId.ToString(), jwt.Claims.Single(c => c.Type == "agent_id").Value);
        Assert.Equal(installationId.ToString(), jwt.Claims.Single(c => c.Type == "installation_id").Value);
        Assert.Equal("agent", jwt.Claims.Single(c => c.Type == "client_type").Value);
        Assert.Equal("access", jwt.Claims.Single(c => c.Type == "token_use").Value);
        Assert.Equal(
            ["agent_id", "client_type", "installation_id", "token_use"],
            jwt.Claims
                .Where(c => c.Type is not ("iss" or "aud" or "nbf" or "exp" or "iat"))
                .Select(c => c.Type)
                .Order(StringComparer.Ordinal)
                .ToArray());
        Assert.InRange(jwt.ValidTo - jwt.ValidFrom, TimeSpan.FromMinutes(9), TimeSpan.FromMinutes(10));
    }

    [Theory]
    [InlineData(AgentIdentityStatus.Disabled)]
    [InlineData(AgentIdentityStatus.Deactivated)]
    public void TokenExchange_RejectsDisabledOrDeactivatedAgents(AgentIdentityStatus status)
    {
        var service = new AgentTokenService(Options.Create(new AuthenticationOptions
        {
            Jwt = new JwtOptions
            {
                Secret = "agent-token-service-test-secret-32bytes!!",
                Issuer = "RPA.Platform",
                Audience = "RPA.Clients",
                ExpirationMinutes = 60,
            },
        }));

        Assert.Throws<UnauthorizedAccessException>(() => service.GenerateAccessToken(new AgentIdentity
        {
            Id = Guid.NewGuid(),
            LicenseInstallationId = Guid.NewGuid(),
            Name = "agent-1",
            Status = status,
        }));
    }
}

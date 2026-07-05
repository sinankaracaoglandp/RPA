namespace RPA.WebAPI.Tests;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Authentication;

public class AuthenticationTests
{
    private static IOptions<AuthenticationOptions> BuildOptions() =>
        Options.Create(new AuthenticationOptions
        {
            Jwt = new JwtOptions
            {
                Secret = "unit-test-secret-key-with-32+bytes-length!!",
                Issuer = "RPA.Platform",
                Audience = "RPA.Clients",
                ExpirationMinutes = 60,
            },
        });

    /// <summary>Fake connector: testuser/testpass → Developer grubu.</summary>
    private static ILdapConnector FakeConnector()
    {
        var mock = new Mock<ILdapConnector>();
        mock.Setup(c => c.AuthenticateAndGetGroupsAsync("testuser", "testpass"))
            .ReturnsAsync(new LdapAuthOutcome
            {
                Authenticated = true,
                Groups = new List<string> { "Developer" },
            });
        mock.Setup(c => c.AuthenticateAndGetGroupsAsync(
                It.IsAny<string>(),
                It.Is<string>(p => p != "testpass")))
            .ReturnsAsync(new LdapAuthOutcome { Authenticated = false });
        return mock.Object;
    }

    private static LdapAuthService BuildService() =>
        new(FakeConnector(),
            new JwtTokenService(BuildOptions()),
            NullLogger<LdapAuthService>.Instance);

    [Fact]
    public async Task Authenticate_ValidUser_ReturnsSuccessAndDeveloperRole()
    {
        var service = BuildService();

        var result = await service.AuthenticateAsync("testuser", "testpass");

        Assert.True(result.Success);
        Assert.NotNull(result.JwtToken);
        Assert.Contains("Developer", result.Roles);
    }

    [Fact]
    public async Task Authenticate_WrongPassword_ReturnsFailure()
    {
        var service = BuildService();

        var result = await service.AuthenticateAsync("testuser", "wrongpass");

        Assert.False(result.Success);
        Assert.Null(result.JwtToken);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task Authenticate_EmptyCredentials_ReturnsFailure()
    {
        var service = BuildService();

        var result = await service.AuthenticateAsync("", "");

        Assert.False(result.Success);
    }

    [Fact]
    public void JwtToken_ContainsSubAndRoleClaims()
    {
        var tokenService = new JwtTokenService(BuildOptions());

        var token = tokenService.GenerateToken("testuser", new[] { "Developer", "Viewer" });

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal("RPA.Platform", jwt.Issuer);
        Assert.Equal("RPA.Clients", jwt.Audiences.First());
        Assert.Equal("testuser", jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Sub).Value);

        var roles = jwt.Claims
            .Where(c => c.Type == ClaimTypes.Role || c.Type == "role")
            .Select(c => c.Value)
            .ToList();
        Assert.Contains("Developer", roles);
        Assert.Contains("Viewer", roles);
        Assert.True(jwt.ValidTo > DateTime.UtcNow);
    }

    // ---- SECURITY: JWT Vulnerabilities ----

    [Fact]
    public void JwtToken_MissingSecret_ThrowsException()
    {
        // CRITICAL FIX: No fallback to all-zero key when secret is missing
        var options = Options.Create(new AuthenticationOptions
        {
            Jwt = new JwtOptions
            {
                Secret = null, // or empty string
                Issuer = "RPA.Platform",
                Audience = "RPA.Clients",
                ExpirationMinutes = 60,
            },
        });

        var tokenService = new JwtTokenService(options);

        // Act & Assert: Must throw, not default to all-zero key
        Assert.Throws<InvalidOperationException>(() =>
            tokenService.GenerateToken("user", new[] { "Developer" })
        );
    }

    [Fact]
    public void JwtToken_ShortSecret_ThrowsException()
    {
        // CRITICAL FIX: Secret must be 32 bytes minimum
        var options = Options.Create(new AuthenticationOptions
        {
            Jwt = new JwtOptions
            {
                Secret = "short", // < 32 bytes
                Issuer = "RPA.Platform",
                Audience = "RPA.Clients",
                ExpirationMinutes = 60,
            },
        });

        var tokenService = new JwtTokenService(options);

        // Act & Assert: Must throw
        Assert.Throws<InvalidOperationException>(() =>
            tokenService.GenerateToken("user", new[] { "Developer" })
        );
    }

    [Fact]
    public void JwtToken_ValidSecret_GeneratesToken()
    {
        // CRITICAL FIX: Valid secret (32+ bytes) should work
        var options = Options.Create(new AuthenticationOptions
        {
            Jwt = new JwtOptions
            {
                Secret = "this-is-a-valid-secret-key-32bytes!!!",
                Issuer = "RPA.Platform",
                Audience = "RPA.Clients",
                ExpirationMinutes = 60,
            },
        });

        var tokenService = new JwtTokenService(options);

        // Act
        var token = tokenService.GenerateToken("user", new[] { "Developer" });

        // Assert
        Assert.NotNull(token);
        Assert.NotEmpty(token);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal("RPA.Platform", jwt.Issuer);
    }
}

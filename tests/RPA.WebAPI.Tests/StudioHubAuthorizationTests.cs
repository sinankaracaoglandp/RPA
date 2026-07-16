namespace RPA.WebAPI.Tests;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Authentication;

public class StudioHubAuthorizationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public StudioHubAuthorizationTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task AgentToken_CannotInvokeStudioSpyCommands()
    {
        var connection = BuildConnection("/hubs/studio", AgentToken());

        await connection.StartAsync();

        await Assert.ThrowsAnyAsync<Exception>(() => connection.InvokeAsync(
            "StartSpy", Guid.NewGuid(), "desktop", null));
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task StudioToken_CannotInvokeRobotHubMethods()
    {
        var robotService = new Mock<IRobotService>();
        var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IRobotService));
            if (descriptor is not null) services.Remove(descriptor);
            services.AddScoped(_ => robotService.Object);
        }));
        var connection = BuildConnection("/hubs/robot", StudioToken("Designer"), factory);

        await connection.StartAsync();

        await Assert.ThrowsAnyAsync<Exception>(() => connection.InvokeAsync(
            "Register", new { MachineName = "RPA-HUB", Mode = "Unattended", Capacity = 1 }));
        robotService.Verify(x => x.RegisterAsync(It.IsAny<RobotRegistrationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        await connection.DisposeAsync();
    }

    private HubConnection BuildConnection(string path, string token, WebApplicationFactory<Program>? factory = null)
    {
        factory ??= _factory;
        var server = factory.Server;
        return new HubConnectionBuilder()
            .WithUrl($"{server.BaseAddress}{path.TrimStart('/')}", options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();
    }

    private string StudioToken(params string[] roles)
    {
        using var scope = _factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<AuthenticationOptions>>();
        return new JwtTokenService(options).GenerateToken("designer", roles);
    }

    private string AgentToken()
    {
        using var scope = _factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<AuthenticationOptions>>().Value.Jwt;
        var now = DateTime.UtcNow;
        var key = new SymmetricSecurityKey(Derive(options.Secret!));
        var token = new JwtSecurityToken(options.Issuer, options.Audience,
            new[]
            {
                new Claim("agent_id", Guid.NewGuid().ToString()),
                new Claim("installation_id", Guid.NewGuid().ToString()),
                new Claim("client_type", "agent"),
                new Claim("token_use", "access"),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            },
            now, now.AddMinutes(10), new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static byte[] Derive(string secret)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes("RPA.JwtTokenService.v1"),
            10000,
            HashAlgorithmName.SHA256,
            32);
    }
}

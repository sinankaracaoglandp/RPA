namespace RPA.WebAPI.Tests;

using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Authentication;

/// <summary>
/// RobotHub SignalR bağlantı ve kimlik doğrulama testleri (Task 3.1, Spec Bölüm 9).
/// </summary>
public class RobotHubTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RobotHubTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private (WebApplicationFactory<Program> factory, Mock<IRobotService> robotMock) BuildFactory()
    {
        var robotMock = new Mock<IRobotService>();
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IRobotService));
                if (descriptor is not null) services.Remove(descriptor);
                services.AddScoped(_ => robotMock.Object);
            });
        });
        return (factory, robotMock);
    }

    private HubConnection BuildConnection(WebApplicationFactory<Program> factory, string? token)
    {
        var server = factory.Server;
        return new HubConnectionBuilder()
            .WithUrl($"{server.BaseAddress}hubs/robot", options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                if (token is not null) options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();
    }

    private string GenerateToken(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var opts = scope.ServiceProvider.GetRequiredService<IOptions<AuthenticationOptions>>();
        var tokenService = new JwtTokenService(opts);
        return tokenService.GenerateToken("robot-agent", new[] { "Robot" });
    }

    [Fact]
    public async Task Connect_WithoutToken_IsRejected()
    {
        var (factory, _) = BuildFactory();
        var connection = BuildConnection(factory, token: null);

        await Assert.ThrowsAnyAsync<Exception>(() => connection.StartAsync());
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task Connect_WithValidToken_Succeeds()
    {
        var (factory, _) = BuildFactory();
        var token = GenerateToken(factory);
        var connection = BuildConnection(factory, token);

        await connection.StartAsync();
        Assert.Equal(HubConnectionState.Connected, connection.State);
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task Register_OverHub_InvokesServiceAndReturnsRobot()
    {
        var (factory, robotMock) = BuildFactory();
        var robot = new Robot { MachineName = "RPA-HUB", Status = RobotStatus.Online, LastHeartbeat = DateTime.UtcNow };
        robotMock.Setup(s => s.RegisterAsync(It.IsAny<RobotRegistrationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(robot);

        var token = GenerateToken(factory);
        var connection = BuildConnection(factory, token);
        var tcs = new TaskCompletionSource<string>();
        connection.On<object>("Registered", payload => tcs.TrySetResult(payload?.ToString() ?? ""));

        await connection.StartAsync();
        await connection.InvokeAsync("Register", new { MachineName = "RPA-HUB", Mode = "Unattended", Capacity = 1 });

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Equal(tcs.Task, completed);
        robotMock.Verify(s => s.RegisterAsync(It.IsAny<RobotRegistrationRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        await connection.DisposeAsync();
    }
}

namespace RPA.WebAPI.Tests;

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RPA.Domain.Interfaces;

/// <summary>
/// AuthController HTTP-seviyesi testleri. IAuthenticationService stub'lanır;
/// gerçek middleware pipeline (auth + CORS) üzerinden çalışır.
/// </summary>
public class AuthControllerIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AuthControllerIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient(IAuthenticationService authService)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(IAuthenticationService));
                if (descriptor is not null) services.Remove(descriptor);
                services.AddScoped(_ => authService);
            });
        }).CreateClient();
    }

    private static IAuthenticationService StubAuth()
    {
        var mock = new Mock<IAuthenticationService>();
        mock.Setup(a => a.AuthenticateAsync("testuser", "testpass"))
            .ReturnsAsync(AuthenticationResult.Ok("fake.jwt.token", new List<string> { "Developer" }));
        mock.Setup(a => a.AuthenticateAsync(
                It.IsAny<string>(), It.Is<string>(p => p != "testpass")))
            .ReturnsAsync(AuthenticationResult.Fail("Geçersiz kullanıcı adı veya şifre."));
        return mock.Object;
    }

    [Fact]
    public async Task Login_ValidCredentials_Returns200WithToken()
    {
        var client = CreateClient(StubAuth());

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "testuser", password = "testpass" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LoginResult>();
        Assert.NotNull(body);
        Assert.Equal("fake.jwt.token", body!.Token);
        Assert.Contains("Developer", body.Roles);
    }

    [Fact]
    public async Task Login_WrongCredentials_Returns401()
    {
        var client = CreateClient(StubAuth());

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "testuser", password = "nope" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_EmptyInput_Returns400()
    {
        var client = CreateClient(StubAuth());

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "", password = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_Returns401()
    {
        var client = CreateClient(StubAuth());

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private class LoginResult
    {
        public string Token { get; set; } = string.Empty;
        public List<string> Roles { get; set; } = new();
    }
}

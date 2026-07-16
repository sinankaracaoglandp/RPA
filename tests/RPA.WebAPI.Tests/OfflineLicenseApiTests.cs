namespace RPA.WebAPI.Tests;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;
using RPA.Domain.Entities;
using RPA.Domain.Exceptions;
using RPA.Domain.Interfaces;
using RPA.Domain.Licensing;
using RPA.Infrastructure.Authentication;
using RPA.WebAPI.Licensing;

public class OfflineLicenseApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public OfflineLicenseApiTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Import_AlteredOrWrongInstallationLicense_ReturnsBadRequest()
    {
        var license = new Mock<ILicenseService>();
        license.Setup(x => x.ImportAsync(It.IsAny<SignedLicenseDocument>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException("LICENSE_INSTALLATION_MISMATCH"));

        var client = CreateClient(license.Object, new Mock<IAgentIdentityRepository>().Object);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserToken("Administrator"));

        var response = await client.PostAsJsonAsync("/api/license/import", SignedLicense());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("LICENSE_INSTALLATION_MISMATCH", body);
    }

    [Fact]
    public async Task CreateActivationCode_AdministratorReceivesOneTimeCode()
    {
        var installationId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var repo = new Mock<IAgentIdentityRepository>();
        var activationCodes = new Mock<IAgentActivationCodeStore>();
        repo.Setup(x => x.GetByIdAsync(agentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentIdentity { Id = agentId, LicenseInstallationId = installationId, Name = "agent-1" });

        var client = CreateClient(Mock.Of<ILicenseService>(), repo.Object, activationCodes.Object);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserToken("Administrator"));

        var response = await client.PostAsync($"/api/agents/{agentId}/activation-code", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ActivationCodeResponse>();
        Assert.Equal(agentId, body!.AgentId);
        Assert.False(string.IsNullOrWhiteSpace(body.ActivationCode));
        activationCodes.Verify(x => x.CreateAsync(
            agentId,
            It.Is<string>(hash => hash != body.ActivationCode && !string.IsNullOrWhiteSpace(hash)),
            It.Is<DateTimeOffset>(expiresAt => expiresAt > DateTimeOffset.UtcNow.AddMinutes(14) && expiresAt <= DateTimeOffset.UtcNow.AddMinutes(15)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateActivationCode_DesignerIsForbidden()
    {
        var client = CreateClient(Mock.Of<ILicenseService>(), Mock.Of<IAgentIdentityRepository>());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserToken("Designer"));

        var response = await client.PostAsync($"/api/agents/{Guid.NewGuid()}/activation-code", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private HttpClient CreateClient(
        ILicenseService license,
        IAgentIdentityRepository agents,
        IAgentActivationCodeStore? activationCodes = null)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                Replace(services, license);
                Replace(services, agents);
                Replace(services, activationCodes ?? Mock.Of<IAgentActivationCodeStore>());
            });
        }).CreateClient();
    }

    private string UserToken(params string[] roles)
    {
        using var scope = _factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<AuthenticationOptions>>();
        return new JwtTokenService(options).GenerateToken("studio-user", roles);
    }

    private static SignedLicenseDocument SignedLicense() =>
        new(OfflineLicensePayload.Create("LIC-1", 1, "ACME", "wrong-installation", "ABC", 1,
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(30), ["agent"]), "altered");

    private static void Replace<T>(IServiceCollection services, T instance) where T : class
    {
        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(T));
        if (descriptor is not null) services.Remove(descriptor);
        services.AddScoped(_ => instance);
    }

    private sealed class ActivationCodeResponse
    {
        public Guid AgentId { get; set; }
        public string ActivationCode { get; set; } = "";
    }
}

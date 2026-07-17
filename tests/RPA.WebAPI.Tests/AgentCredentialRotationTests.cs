namespace RPA.WebAPI.Tests;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Infrastructure.Authentication;
using RPA.Infrastructure.Persistence;
using RPA.WebAPI.Authentication;
using RPA.WebAPI.Licensing;

/// <summary>
/// POST /api/agents/{id}/rotate-credential — Offline Agent Licensing tasarim spec'i:
/// "Credential rotation invalidates the previous credential immediately."
/// Testler gercek EF (InMemory) + gercek EfAgentIdentityRepository uzerinden kosar; boylece
/// rotasyonun token degisim yolunu (AgentAuthController) gercekten etkiledigi kanitlanir.
/// </summary>
public class AgentCredentialRotationTests
{
    private const string OldCredential = "OLD-CREDENTIAL-PLAINTEXT";

    [Fact]
    public async Task Rotate_ReturnsNewCredentialOnceAndPersistsOnlyItsHash()
    {
        var agentId = Guid.NewGuid();
        await using var app = CreateApp(agentId, AgentIdentityStatus.Activated);
        var client = AdminClient(app);

        var response = await client.PostAsync($"/api/agents/{agentId}/rotate-credential", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var credential = body.GetProperty("credential").GetString();
        Assert.Equal(agentId, body.GetProperty("agentId").GetGuid());
        Assert.False(string.IsNullOrWhiteSpace(credential));
        Assert.NotEqual(OldCredential, credential);

        using var scope = app.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<RpaDbContext>()
            .AgentIdentities.SingleAsync(x => x.Id == agentId);
        Assert.NotNull(stored.CredentialHash);
        Assert.NotEqual(credential, stored.CredentialHash);
        Assert.NotEqual(SecretHasher.Hash(OldCredential), stored.CredentialHash);
        Assert.Equal(SecretHasher.Hash(credential!), stored.CredentialHash);
    }

    /// <summary>
    /// Token degisim yolunu surdugu icin GERCEK lisansli uygulama kullanir: agent token'i artik
    /// lisansin gecerli oldugunu ve agent'in bu kuruluma ait oldugunu da dogruluyor.
    /// </summary>
    [Fact]
    public async Task Rotate_InvalidatesPreviousCredentialImmediately()
    {
        var agentId = Guid.NewGuid();
        await using var licensed = await LicensedTestApp.CreateAsync();
        await licensed.AddAgentAsync(agentId, AgentIdentityStatus.Activated, SecretHasher.Hash(OldCredential));
        var client = licensed.AdminClient();

        // Rotasyondan ONCE eski credential calisiyor.
        var before = await client.PostAsJsonAsync("/api/agent-auth/token", new AgentTokenRequest(agentId, OldCredential));
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        var rotate = await client.PostAsync($"/api/agents/{agentId}/rotate-credential", null);
        var credential = (await rotate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("credential").GetString();

        // Rotasyondan SONRA eski credential derhal reddedilir.
        var afterOld = await client.PostAsJsonAsync("/api/agent-auth/token", new AgentTokenRequest(agentId, OldCredential));
        Assert.Equal(HttpStatusCode.Unauthorized, afterOld.StatusCode);
        Assert.Contains("AGENT_CREDENTIAL_INVALID", await afterOld.Content.ReadAsStringAsync());

        // Yeni credential calisir.
        var afterNew = await client.PostAsJsonAsync("/api/agent-auth/token", new AgentTokenRequest(agentId, credential!));
        Assert.Equal(HttpStatusCode.OK, afterNew.StatusCode);
        var token = (await afterNew.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        Assert.Equal(agentId.ToString(), new JwtSecurityTokenHandler().ReadJwtToken(token).Claims
            .Single(c => c.Type == "agent_id").Value);
    }

    [Theory]
    [InlineData(AgentIdentityStatus.PendingActivation)]
    [InlineData(AgentIdentityStatus.Disabled)]
    [InlineData(AgentIdentityStatus.Deactivated)]
    public async Task Rotate_NonActivatedAgent_IsRejectedAndLeavesCredentialUntouched(AgentIdentityStatus status)
    {
        var agentId = Guid.NewGuid();
        await using var app = CreateApp(agentId, status);
        var client = AdminClient(app);

        var response = await client.PostAsync($"/api/agents/{agentId}/rotate-credential", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("AGENT_NOT_ACTIVATED", await response.Content.ReadAsStringAsync());

        using var scope = app.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<RpaDbContext>()
            .AgentIdentities.SingleAsync(x => x.Id == agentId);
        Assert.Equal(SecretHasher.Hash(OldCredential), stored.CredentialHash);
    }

    [Fact]
    public async Task Rotate_UnknownAgent_ReturnsNotFound()
    {
        await using var app = CreateApp(Guid.NewGuid(), AgentIdentityStatus.Activated);
        var client = AdminClient(app);

        var response = await client.PostAsync($"/api/agents/{Guid.NewGuid()}/rotate-credential", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Rotate_DesignerIsForbidden()
    {
        var agentId = Guid.NewGuid();
        await using var app = CreateApp(agentId, AgentIdentityStatus.Activated);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserToken(app, "Designer"));

        var response = await client.PostAsync($"/api/agents/{agentId}/rotate-credential", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Rotate_AnonymousIsUnauthorized()
    {
        var agentId = Guid.NewGuid();
        await using var app = CreateApp(agentId, AgentIdentityStatus.Activated);

        var response = await app.CreateClient().PostAsync($"/api/agents/{agentId}/rotate-credential", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateApp(Guid agentId, AgentIdentityStatus status)
    {
        var databaseName = $"agent-rotation-{Guid.NewGuid()}";
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptors = services
                    .Where(d => d.ServiceType == typeof(DbContextOptions<RpaDbContext>)
                        || d.ServiceType == typeof(DbContextOptions)
                        || d.ServiceType == typeof(RpaDbContext)
                        || (d.ServiceType.FullName?.StartsWith("Microsoft.EntityFrameworkCore.", StringComparison.Ordinal) ?? false)
                        || (d.ServiceType.Namespace?.StartsWith("Npgsql", StringComparison.Ordinal) ?? false))
                    .ToList();
                foreach (var descriptor in descriptors) services.Remove(descriptor);

                // Npgsql saglayicisi Program.cs'te kayitli; testte yalnizca InMemory saglayicisi
                // olsun diye EF ic servis saglayicisi izole edilir.
                var efProvider = new ServiceCollection().AddEntityFrameworkInMemoryDatabase().BuildServiceProvider();
                services.AddDbContext<RpaDbContext>(options => options
                    .UseInMemoryDatabase(databaseName)
                    .UseInternalServiceProvider(efProvider));
            });
        });

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RpaDbContext>();
        db.AgentIdentities.Add(new AgentIdentity
        {
            Id = agentId,
            LicenseInstallationId = Guid.NewGuid(),
            Name = "agent-1",
            Status = status,
            MachineFingerprint = "FP-1",
            CredentialHash = SecretHasher.Hash(OldCredential),
        });
        db.SaveChanges();
        return factory;
    }

    private static HttpClient AdminClient(WebApplicationFactory<Program> app)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserToken(app, "Administrator"));
        return client;
    }

    private static string UserToken(WebApplicationFactory<Program> app, params string[] roles)
    {
        using var scope = app.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<AuthenticationOptions>>();
        return new JwtTokenService(options).GenerateToken("studio-user", roles);
    }
}

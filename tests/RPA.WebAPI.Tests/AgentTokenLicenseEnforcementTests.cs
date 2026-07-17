namespace RPA.WebAPI.Tests;

using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RPA.Domain.Enums;
using RPA.Infrastructure.Persistence;
using RPA.WebAPI.Authentication;
using RPA.WebAPI.Licensing;

/// <summary>
/// Review bulgusu: token degisimi lisansa HIC bakmiyordu — yalnizca agent.Status kontrol
/// ediliyordu. Sonuc: lisansin suresi doldugunda ajanlar 10 dakikalik tokeni sonsuza dek
/// yenileyip calismaya devam ediyordu; sona erme yalnizca import ve aktivasyon yollarinda
/// zorlaniyordu. Sona erme, kimlik dogrulama yolunda da zorlanmalidir.
/// </summary>
public sealed class AgentTokenLicenseEnforcementTests
{
    private const string Credential = "AGENT-CREDENTIAL-PLAINTEXT";

    [Fact]
    public async Task Token_WithValidLicense_IsIssued()
    {
        var agentId = Guid.NewGuid();
        await using var app = await LicensedTestApp.CreateAsync(expiresAt: DateTimeOffset.UtcNow.AddDays(30));
        await app.AddAgentAsync(agentId, AgentIdentityStatus.Activated, SecretHasher.Hash(Credential));

        var response = await app.CreateClient().PostAsJsonAsync("/api/agent-auth/token",
            new AgentTokenRequest(agentId, Credential));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Token_WithExpiredLicense_IsRefused()
    {
        var agentId = Guid.NewGuid();
        await using var app = await LicensedTestApp.CreateAsync(expiresAt: DateTimeOffset.UtcNow.AddSeconds(-1));
        await app.AddAgentAsync(agentId, AgentIdentityStatus.Activated, SecretHasher.Hash(Credential));

        var response = await app.CreateClient().PostAsJsonAsync("/api/agent-auth/token",
            new AgentTokenRequest(agentId, Credential));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("LICENSE_EXPIRED", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Token_ForAgentOfAnotherInstallation_IsRefused()
    {
        var agentId = Guid.NewGuid();
        await using var app = await LicensedTestApp.CreateAsync();
        await app.AddAgentAsync(agentId, AgentIdentityStatus.Activated, SecretHasher.Hash(Credential));

        // Agent baska bir kuruluma tasinir (kopyalanmis/karisik veritabani senaryosu).
        using (var scope = app.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RpaDbContext>();
            var agent = await db.AgentIdentities.SingleAsync(x => x.Id == agentId);
            agent.LicenseInstallationId = Guid.NewGuid();
            await db.SaveChangesAsync();
        }

        var response = await app.CreateClient().PostAsJsonAsync("/api/agent-auth/token",
            new AgentTokenRequest(agentId, Credential));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("LICENSE_INSTALLATION_MISMATCH", await response.Content.ReadAsStringAsync());
    }
}

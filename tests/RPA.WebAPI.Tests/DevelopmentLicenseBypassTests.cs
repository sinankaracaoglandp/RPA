namespace RPA.WebAPI.Tests;

using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using RPA.Domain.Licensing;
using RPA.Infrastructure.Licensing;
using RPA.WebAPI.Authentication;
using RPA.WebAPI.Licensing;

/// <summary>
/// DEBUG derlemesinde lisans anahtari zorunlu DEGILDIR: gelistirici hicbir lisans dosyasi
/// ice aktarmadan agent olusturup aktive edebilir ve token alabilir. RELEASE derlemesinde
/// bypass HIC derlenmez — anahtar her zaman zorunludur.
/// </summary>
public sealed class DevelopmentLicenseBypassTests
{
    private static IConfiguration Config(string? value) =>
        new ConfigurationBuilder().AddInMemoryCollection(
            value is null
                ? []
                : new Dictionary<string, string?> { [DevelopmentLicenseBypass.ConfigurationKey] = value })
            .Build();

    [Fact]
    public void Bypass_DefaultsToBuildConfiguration()
    {
#if DEBUG
        Assert.True(DevelopmentLicenseBypass.IsEnabled(Config(null)));
#else
        Assert.False(DevelopmentLicenseBypass.IsEnabled(Config(null)));
#endif
    }

    [Fact]
    public void Bypass_IsNeverEnabledInRelease_AndCanBeTurnedOffInDebug()
    {
        // Yapilandirma DEBUG'da kapatabilir; RELEASE'te acamaz.
        Assert.False(DevelopmentLicenseBypass.IsEnabled(Config("false")));
#if !DEBUG
        Assert.False(DevelopmentLicenseBypass.IsEnabled(Config("true")));
#endif
    }

#if DEBUG
    [Fact]
    public async Task WithoutLicense_StatusIsValidDevelopmentEdition()
    {
        await using var app = await LicensedTestApp.CreateAsync(seedLicense: false, developmentBypass: true);

        // LicenseStatus istemci tarafinda deserialize EDILEMEZ (ctor parametreleri ile
        // ozellikler birebir eslesmiyor) — yanit ham JSON olarak okunur.
        var status = JsonDocument.Parse(await app.AdminClient().GetStringAsync("/api/license/status")).RootElement;

        Assert.True(status.GetProperty("isInstalled").GetBoolean());
        Assert.True(status.GetProperty("isValid").GetBoolean());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("errorCode").ValueKind);
        Assert.Equal(DevelopmentLicenseService.DevelopmentEdition, status.GetProperty("edition").GetString());
    }

    [Fact]
    public async Task WithoutLicense_AgentCanBeCreatedActivatedAndGetToken()
    {
        await using var app = await LicensedTestApp.CreateAsync(seedLicense: false, developmentBypass: true);
        var admin = app.AdminClient();

        var created = await admin.PostAsJsonAsync("/api/agents", new CreateAgentRequest("dev-agent"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var agent = await created.Content.ReadFromJsonAsync<AgentDto>();

        var codeResponse = await admin.PostAsync($"/api/agents/{agent!.Id}/activation-code", null);
        Assert.Equal(HttpStatusCode.OK, codeResponse.StatusCode);
        var code = await codeResponse.Content.ReadFromJsonAsync<ActivationCodeResponse>();

        var installation = await admin.GetFromJsonAsync<InstallationRequestDocument>("/api/license/installation-request");

        var activated = await app.CreateClient().PostAsJsonAsync("/api/agent-auth/activate",
            new ActivateAgentRequest(agent.Id, installation!.InstallationId, code!.ActivationCode, "FP-DEV"));
        Assert.Equal(HttpStatusCode.OK, activated.StatusCode);
        var credential = await activated.Content.ReadFromJsonAsync<ActivateAgentResponse>();

        var token = await app.CreateClient().PostAsJsonAsync("/api/agent-auth/token",
            new AgentTokenRequest(agent.Id, credential!.Credential));

        Assert.Equal(HttpStatusCode.OK, token.StatusCode);
    }

    [Fact]
    public async Task BypassDisabled_WithoutLicense_TokenIsStillRefused()
    {
        // Aynı kurulum, bypass kapali: DEBUG'da bile lisanssiz token verilmez.
        await using var app = await LicensedTestApp.CreateAsync(seedLicense: false, developmentBypass: false);

        var status = JsonDocument.Parse(await app.AdminClient().GetStringAsync("/api/license/status")).RootElement;

        Assert.False(status.GetProperty("isValid").GetBoolean());
        Assert.Equal("LICENSE_MISSING", status.GetProperty("errorCode").GetString());
    }
#endif
}

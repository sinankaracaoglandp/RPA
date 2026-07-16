namespace RPA.WebAPI.Tests;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RPA.Domain.Entities;
using RPA.Domain.Exceptions;
using RPA.Domain.Interfaces;
using RPA.Domain.Licensing;
using RPA.Infrastructure.Authentication;
using RPA.Infrastructure.Licensing;
using RPA.Infrastructure.Persistence;
using RPA.Infrastructure.Workflow;
using RPA.WebAPI.Authentication;
using RPA.WebAPI.Licensing;

/// <summary>
/// Task 10 — TEK musteri yolculugu, uctan uca (Offline Agent Licensing kabul kriterleri).
///
/// GERCEK olan: kurulum kimligi (gercek RSA-3072 + DpapiInstallationKeyStore, gecici dizin),
/// test-saticinin gercek RSA anahtar cifti ile URETIM kanonik serilestiricisi uzerinden imza,
/// gercek LicenseService/EfAgentIdentityRepository/EF, gercek HTTP uclari, gercek SignalR
/// baglantilari ve gercek JWT dogrulamasi.
///
/// SIMULE olan: yalniz "15 dakika offline" adimi — sahte saat kullanilir (gercekten 15 dk
/// beklenmez) ve kira kapisi burada ConnectivityLease semantigini birebir yansitan bir test
/// ikizidir; ConnectivityLease'in kendisi RPA.Agent (net10.0-windows) icinde yasar ve bu
/// net10.0 test projesinden REFERANS EDILEMEZ. Kiranin kendi davranisi RPA.Agent.Tests'te,
/// BaseRunner'in kapiyi her node oncesi cagirdigi RPA.Infrastructure.Tests'te dogrulanir;
/// burada dogrulanan, GERCEK BaseRunner'in kira dolunca sonraki node'u BASLATMAMASIDIR.
/// </summary>
public sealed class OfflineLicensingEndToEndTests : IDisposable
{
    private const string LicensedSeats = "2";
    private readonly string _keyDirectory = Path.Combine(Path.GetTempPath(), "rpa-e2e-" + Guid.NewGuid().ToString("N"));
    private readonly RSA _vendorKey = RSA.Create(3072);

    public void Dispose()
    {
        _vendorKey.Dispose();
        if (Directory.Exists(_keyDirectory)) Directory.Delete(_keyDirectory, recursive: true);
    }

    [Fact]
    public async Task CompleteCustomerJourney_FromInstallationRequestToReplacementAgent()
    {
        await using var app = CreateApp();
        var admin = AdminClient(app);

        // 1) Musteri kurulum talebini disa aktarir (gercek kurulum kimligi olusur).
        var request = await admin.GetFromJsonAsync<InstallationRequestDocument>("/api/license/installation-request");
        Assert.NotNull(request);
        Assert.False(string.IsNullOrWhiteSpace(request!.InstallationId));
        Assert.False(string.IsNullOrWhiteSpace(request.InstallationPublicKeyFingerprint));

        // 2) Satici (bu testte test-satici anahtari) 2 koltukluk lisansi imzalar.
        var license = SignLicense(request, revision: 1, seats: int.Parse(LicensedSeats));

        // 3) Musteri lisansi Studio uzerinden ice aktarir.
        var import = await admin.PostAsJsonAsync("/api/license/import", license);
        Assert.Equal(HttpStatusCode.OK, import.StatusCode);

        var status = await Status(admin);
        Assert.True(status.GetProperty("isValid").GetBoolean());
        Assert.Equal(2, status.GetProperty("maxActivatedAgents").GetInt32());
        Assert.Equal(0, status.GetProperty("activatedAgents").GetInt32());
        Assert.Equal("enterprise", status.GetProperty("edition").GetString());
        Assert.Equal("ACME Sanayi A.S.", status.GetProperty("customerName").GetString());

        // 4) Tam olarak lisansli sayida agent aktive edilir.
        var first = await ActivateAgent(admin, request.InstallationId, "agent-1");
        var second = await ActivateAgent(admin, request.InstallationId, "agent-2");

        Assert.Equal(2, (await Status(admin)).GetProperty("activatedAgents").GetInt32());

        // 5) Bir sonraki aktivasyon reddedilir — koltuk kalmadi.
        var third = await CreateAgent(admin, "agent-3");
        var thirdCode = await CreateActivationCode(admin, third);
        var rejected = await admin.PostAsJsonAsync("/api/agent-auth/activate",
            new ActivateAgentRequest(third, request.InstallationId, thirdCode, "FP-3"));
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        Assert.Contains("AGENT_LICENSE_LIMIT_REACHED", await rejected.Content.ReadAsStringAsync());

        // 6) Aktive agent kisa omurlu JWT alir.
        var agentToken = await ExchangeToken(app, first.AgentId, first.Credential);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(agentToken);
        Assert.Equal(first.AgentId.ToString(), jwt.Claims.Single(c => c.Type == "agent_id").Value);
        Assert.Equal("agent", jwt.Claims.Single(c => c.Type == "client_type").Value);
        Assert.InRange(jwt.ValidTo - jwt.ValidFrom, TimeSpan.FromMinutes(9), TimeSpan.FromMinutes(10));

        // 7) SignalR: agent ve Studio yetkileri AYRISIR.
        await AssertHubPermissionSeparation(app, agentToken);

        // 8) Node sinirinda 15 dakika offline — calisan node biter, SONRAKI node baslamaz.
        AssertSuspendsAtNodeBoundaryAfterFifteenMinutesOffline();

        // 9) Bir agent devre disi birakilir (deactivate) — koltuk serbest kalir.
        var deactivate = await admin.PostAsync($"/api/agents/{second.AgentId}/deactivate", null);
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);
        Assert.Equal(1, (await Status(admin)).GetProperty("activatedAgents").GetInt32());

        // Devre disi agent artik token alamaz.
        var revoked = await admin.PostAsJsonAsync("/api/agent-auth/token",
            new AgentTokenRequest(second.AgentId, second.Credential));
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);

        // 10) Yerine yeni agent aktive edilir — bosalan koltugu alir.
        var replacementCode = await CreateActivationCode(admin, third);
        var replacement = await admin.PostAsJsonAsync("/api/agent-auth/activate",
            new ActivateAgentRequest(third, request.InstallationId, replacementCode, "FP-3"));
        Assert.Equal(HttpStatusCode.OK, replacement.StatusCode);

        Assert.Equal(2, (await Status(admin)).GetProperty("activatedAgents").GetInt32());
    }

    private static async Task<JsonElement> Status(HttpClient admin)
    {
        var response = await admin.GetAsync("/api/license/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task CopiedOrEditedLicense_IsRejected()
    {
        await using var app = CreateApp();
        var admin = AdminClient(app);
        var request = (await admin.GetFromJsonAsync<InstallationRequestDocument>("/api/license/installation-request"))!;

        // (a) Baska bir kurulum icin imzalanmis lisans (kopyalama) — imza gecerli, baglama degil.
        var copied = SignLicense(request with { InstallationId = "OTHER-INSTALLATION" }, revision: 1, seats: 2);
        var copiedResponse = await admin.PostAsJsonAsync("/api/license/import", copied);
        Assert.Equal(HttpStatusCode.BadRequest, copiedResponse.StatusCode);
        Assert.Contains("LICENSE_INSTALLATION_MISMATCH", await copiedResponse.Content.ReadAsStringAsync());

        // (b) Koltuk sayisi imzadan SONRA duzenlenmis lisans — imza bozulur.
        var signed = SignLicense(request, revision: 1, seats: 2);
        var edited = signed with { Payload = signed.Payload with { MaxActivatedAgents = 999 } };
        var editedResponse = await admin.PostAsJsonAsync("/api/license/import", edited);
        Assert.Equal(HttpStatusCode.BadRequest, editedResponse.StatusCode);
        Assert.Contains("LICENSE_SIGNATURE_INVALID", await editedResponse.Content.ReadAsStringAsync());
    }

    // --- adim 7: hub yetki ayrisimi -------------------------------------------------------

    private static async Task AssertHubPermissionSeparation(WebApplicationFactory<Program> app, string agentToken)
    {
        // Agent JWT'si robot hub'ina baglanabilir.
        var robot = BuildConnection(app, "/hubs/robot", agentToken);
        await robot.StartAsync();
        await robot.DisposeAsync();

        // Ayni agent JWT'si Studio spy komutlarini CAGIRAMAZ.
        var studio = BuildConnection(app, "/hubs/studio", agentToken);
        await studio.StartAsync();
        await Assert.ThrowsAnyAsync<Exception>(() => studio.InvokeAsync("StartSpy", Guid.NewGuid(), "desktop", null));
        await studio.DisposeAsync();

        // Studio kullanicisi da robot hub metotlarini cagiramaz.
        var designer = BuildConnection(app, "/hubs/robot", UserToken(app, "Designer"));
        await designer.StartAsync();
        await Assert.ThrowsAnyAsync<Exception>(() => designer.InvokeAsync(
            "Register", new { MachineName = "RPA-E2E", Mode = "Unattended", Capacity = 1 }));
        await designer.DisposeAsync();
    }

    private static HubConnection BuildConnection(WebApplicationFactory<Program> app, string path, string token)
    {
        var server = app.Server;
        return new HubConnectionBuilder()
            .WithUrl($"{server.BaseAddress}{path.TrimStart('/')}", options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();
    }

    // --- adim 8: node sinirinda kira -------------------------------------------------------

    private const string TwoNodeWorkflow = """
    {
      "schemaVersion": "1.0",
      "id": "33333333-3333-3333-3333-333333333333",
      "name": "Offline sinir",
      "version": "1.0.0",
      "arguments": { "in": [], "out": [ { "name": "message", "type": "string" } ] },
      "nodes": [
        { "id": "n1", "type": "assign", "variableName": "message", "value": "bir" },
        { "id": "n2", "type": "assign", "variableName": "message", "value": "iki" }
      ],
      "connections": [ { "from": "n1", "to": "n2" } ]
    }
    """;

    private static void AssertSuspendsAtNodeBoundaryAfterFifteenMinutesOffline()
    {
        var clock = new TestClock(DateTimeOffset.UnixEpoch);
        var gate = new LeaseGate(clock);
        var runner = new BaseRunner(new WorkflowValidator(), new ActivityCatalog(), new EmptyActivityFactory(),
            NullLogger<BaseRunner>.Instance, vault: null, continuationGate: gate);
        var jobRunId = Guid.NewGuid();

        // n1 baslar; sonrasinda baglanti kopar ve tam 15 dk gecer → n2 BASLAMAZ.
        gate.AfterNode = () => clock.Advance(TimeSpan.FromMinutes(15));

        var result = runner.ExecuteAsync(new WorkflowVersion { JsonDefinition = TwoNodeWorkflow }, new(), jobRunId)
            .GetAwaiter().GetResult();

        Assert.False(result.Success);
        var suspended = Assert.IsType<ExecutionSuspendedException>(result.Exception);
        Assert.Equal(jobRunId, suspended.JobRunId);
        Assert.Equal("n2", suspended.NextNodeId);
        Assert.Equal(["n1", "n2"], gate.Seen);
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now;
        public TestClock(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now += delta;
    }

    /// <summary>ConnectivityLease semantiginin test ikizi (RPA.Agent net10.0-windows'tur, referans edilemez).</summary>
    private sealed class LeaseGate : IExecutionContinuationGate
    {
        private static readonly TimeSpan MaxOfflineInterval = TimeSpan.FromMinutes(15);
        private readonly TestClock _clock;
        private readonly DateTimeOffset _lastValidatedAt;
        public LeaseGate(TestClock clock) { _clock = clock; _lastValidatedAt = clock.GetUtcNow(); }
        public List<string> Seen { get; } = [];
        public Action? AfterNode { get; set; }

        public Task EnsureMayStartNodeAsync(Guid jobRunId, string nodeId, CancellationToken cancellationToken)
        {
            Seen.Add(nodeId);
            if (_clock.GetUtcNow() >= _lastValidatedAt + MaxOfflineInterval)
            {
                throw new ExecutionSuspendedException(jobRunId, nodeId, "Baglanti kirasi doldu.");
            }

            AfterNode?.Invoke();
            return Task.CompletedTask;
        }
    }

    // --- yardimcilar ----------------------------------------------------------------------

    private SignedLicenseDocument SignLicense(InstallationRequestDocument request, int revision, int seats)
    {
        var payload = OfflineLicensePayload.Create("LIC-E2E", revision, "ACME", "ACME Sanayi A.S.", "enterprise",
            request.InstallationId, request.InstallationPublicKeyFingerprint, seats,
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(30), ["agent", "sap"]);

        // URETIM serilestiricisi kullanilir — imzalanan baytlar dogrulananla ayni olsun diye.
        var signature = _vendorKey.SignData(CanonicalLicenseSerializer.SerializePayload(payload),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        return new SignedLicenseDocument(payload, Convert.ToBase64String(signature));
    }

    private static async Task<Guid> CreateAgent(HttpClient admin, string name)
    {
        var response = await admin.PostAsJsonAsync("/api/agents", new CreateAgentRequest(name));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<string> CreateActivationCode(HttpClient admin, Guid agentId)
    {
        var response = await admin.PostAsync($"/api/agents/{agentId}/activation-code", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("activationCode").GetString()!;
    }

    private static async Task<ActivateAgentResponse> ActivateAgent(HttpClient admin, string installationId, string name)
    {
        var agentId = await CreateAgent(admin, name);
        var code = await CreateActivationCode(admin, agentId);
        var response = await admin.PostAsJsonAsync("/api/agent-auth/activate",
            new ActivateAgentRequest(agentId, installationId, code, "FP-" + name));
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = (await response.Content.ReadFromJsonAsync<ActivateAgentResponse>())!;

        // Aktivasyon kodu tek kullanimliktir — ayni kod ikinci kez calismaz.
        var replay = await admin.PostAsJsonAsync("/api/agent-auth/activate",
            new ActivateAgentRequest(agentId, installationId, code, "FP-" + name));
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        return body;
    }

    private static async Task<string> ExchangeToken(WebApplicationFactory<Program> app, Guid agentId, string credential)
    {
        var response = await app.CreateClient().PostAsJsonAsync("/api/agent-auth/token",
            new AgentTokenRequest(agentId, credential));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
    }

    private WebApplicationFactory<Program> CreateApp()
    {
        Directory.CreateDirectory(_keyDirectory);
        var databaseName = "licensing-e2e-" + Guid.NewGuid();
        var vendorPublicKeyPem = new string(PemEncoding.Write("PUBLIC KEY", _vendorKey.ExportSubjectPublicKeyInfo()));

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Licensing:VendorPublicKeyPem", vendorPublicKeyPem);
            builder.UseSetting("Licensing:KeyDirectory", _keyDirectory);
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

                var efProvider = new ServiceCollection().AddEntityFrameworkInMemoryDatabase().BuildServiceProvider();
                services.AddDbContext<RpaDbContext>(options => options
                    .UseInMemoryDatabase(databaseName)
                    // Uretimde Npgsql'dir: gercek islem + "FOR UPDATE" satir kilidi (koltuk yarisi
                    // Infrastructure testlerinde dogrulanir). InMemory saglayicisi islem DESTEKLEMEZ
                    // ve BeginTransactionAsync'i uyari yerine hataya cevirir; burada bastirilan yalniz
                    // O TEST-SAGLAYICI KISITIDIR — uretim davranisi degismez.
                    .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                    .UseInternalServiceProvider(efProvider));

                var robotService = services.SingleOrDefault(d => d.ServiceType == typeof(IRobotService));
                if (robotService is not null) services.Remove(robotService);
                services.AddScoped(_ => Mock.Of<IRobotService>());
            });
        });
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

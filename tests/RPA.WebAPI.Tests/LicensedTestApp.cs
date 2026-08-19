namespace RPA.WebAPI.Tests;

using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.InMemory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using RPA.Domain.Entities;
using RPA.Domain.Interfaces;
using RPA.Domain.Licensing;
using RPA.Infrastructure.Authentication;
using RPA.Infrastructure.Licensing;
using RPA.Infrastructure.Persistence;

/// <summary>
/// Lisansli bir WebAPI test uygulamasi: gercek kurulum kimligi (RSA + DpapiInstallationKeyStore,
/// gecici dizin), test-satici anahtariyla URETIM kanonik serilestiricisi uzerinden imzalanmis
/// gercek lisans ve InMemory EF.
///
/// Neden ortak: agent token degisimi artik lisansin GECERLI oldugunu ve agent'in BU kuruluma ait
/// oldugunu dogruluyor (review bulgusu: suresi dolmus lisansta ajanlar sonsuza dek token
/// yeniliyordu). Bu yuzden token yolunu suren her test gercek bir lisans seed etmek zorundadir —
/// lisanssiz bir agent'in token almasi artik gecerli bir senaryo DEGILDIR.
/// </summary>
public sealed class LicensedTestApp : IAsyncDisposable
{
    private readonly RSA _vendorKey = RSA.Create(3072);
    private readonly string _keyDirectory = Path.Combine(Path.GetTempPath(), "rpa-lic-" + Guid.NewGuid().ToString("N"));

    private LicensedTestApp(WebApplicationFactory<Program> factory) => Factory = factory;

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    /// <summary>Seed edilen lisansin bagli oldugu kurulum satirinin kimligi.</summary>
    public Guid InstallationRowId { get; private set; }

    /// <summary>Kurulum kimligi (imzali yukteki installationId ile ayni).</summary>
    public string InstallationId { get; private set; } = "";

    /// <summary>
    /// Uygulamayi kurar ve <paramref name="expiresAt"/> son kullanma tarihli, <paramref name="seats"/>
    /// koltukluk gercek imzali bir lisans yukler.
    /// </summary>
    /// <param name="realRobotService">
    /// true ise gercek RobotService/EF kullanilir (robot sahiplik testleri gercek veriyi olcer);
    /// false ise IRobotService mock'lanir (lisans yolunu suren testler robot katmanini onemsemez).
    /// </param>
    public static async Task<LicensedTestApp> CreateAsync(
        DateTimeOffset? expiresAt = null,
        int seats = 5,
        bool realRobotService = false,
        Action<IServiceCollection>? configureServices = null,
        bool seedLicense = true,
        bool developmentBypass = false)
    {
        var app = new LicensedTestApp(null!);
        Directory.CreateDirectory(app._keyDirectory);
        var vendorPublicKeyPem = new string(PemEncoding.Write("PUBLIC KEY", app._vendorKey.ExportSubjectPublicKeyInfo()));
        var databaseName = "licensed-app-" + Guid.NewGuid();

        app.Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Licensing:VendorPublicKeyPem", vendorPublicKeyPem);
            builder.UseSetting("Licensing:KeyDirectory", app._keyDirectory);
            // Lisans davranisini olcen testler DEBUG gelistirme bypass'ini KAPATIR; aksi halde
            // "lisans yok/suresi dolmus" senaryolari gelistirme lisansina dusup gecerli gorunurdu.
            builder.UseSetting(DevelopmentLicenseBypass.ConfigurationKey, developmentBypass ? "true" : "false");
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
                    .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                    .UseInternalServiceProvider(efProvider));

                if (!realRobotService)
                {
                    var robotService = services.SingleOrDefault(d => d.ServiceType == typeof(IRobotService));
                    if (robotService is not null) services.Remove(robotService);
                    services.AddScoped(_ => Mock.Of<IRobotService>());
                }

                configureServices?.Invoke(services);
            });
        });

        if (seedLicense) await app.SeedLicenseAsync(expiresAt ?? DateTimeOffset.UtcNow.AddDays(30), seats);
        return app;
    }

    /// <summary>
    /// Kurulum satirini gercek kimlikle olusturur ve imzali lisansi dogrudan kalicilastirir.
    /// (ImportAsync kullanilmaz: suresi DOLMUS lisans senaryosu import ucundan gecemez —
    /// gecmiste import edilip sonradan suresi dolmus bir kurulumu temsil eder.)
    /// </summary>
    private async Task SeedLicenseAsync(DateTimeOffset expiresAt, int seats)
    {
        using var scope = Factory.Services.CreateScope();
        var licenses = scope.ServiceProvider.GetRequiredService<ILicenseService>();
        var request = await licenses.ExportInstallationRequestAsync();
        InstallationId = request.InstallationId;

        var payload = OfflineLicensePayload.Create("LIC-TEST", 1, "ACME", "ACME Sanayi A.S.", "enterprise",
            request.InstallationId, request.InstallationPublicKeyFingerprint, seats,
            DateTimeOffset.UtcNow.AddDays(-1), expiresAt, ["agent"]);
        var signature = _vendorKey.SignData(CanonicalLicenseSerializer.SerializePayload(payload),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pss);

        var db = scope.ServiceProvider.GetRequiredService<RpaDbContext>();
        var installation = await db.LicenseInstallations.SingleAsync(x => x.InstallationId == request.InstallationId);
        installation.SignedLicenseDocument = System.Text.Json.JsonSerializer.Serialize(
            new SignedLicenseDocument(payload, Convert.ToBase64String(signature)));
        installation.InstalledLicenseRevision = 1;
        await db.SaveChangesAsync();
        InstallationRowId = installation.Id;
    }

    /// <summary>Seed edilen kuruluma bagli, aktive edilmis bir agent ekler.</summary>
    public async Task AddAgentAsync(Guid agentId, RPA.Domain.Enums.AgentIdentityStatus status, string credentialHash)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RpaDbContext>();
        db.AgentIdentities.Add(new AgentIdentity
        {
            Id = agentId,
            LicenseInstallationId = InstallationRowId,
            Name = "agent-1",
            Status = status,
            MachineFingerprint = "FP-1",
            CredentialHash = credentialHash,
        });
        await db.SaveChangesAsync();
    }

    public HttpClient CreateClient() => Factory.CreateClient();

    public HttpClient AdminClient()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserToken("Administrator"));
        return client;
    }

    public string UserToken(params string[] roles)
    {
        using var scope = Factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<AuthenticationOptions>>();
        return new JwtTokenService(options).GenerateToken("studio-user", roles);
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        _vendorKey.Dispose();
        if (Directory.Exists(_keyDirectory)) Directory.Delete(_keyDirectory, recursive: true);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using RPA.Agent;
using RPA.Agent.Authentication;
using RPA.Agent.Configuration;
using RPA.Infrastructure.Persistence;
using RPA.Infrastructure.Queues;
using RPA.Infrastructure.Robots;
using RPA.Infrastructure.SAP;
using RPA.Infrastructure.Vault;
using RPA.Infrastructure.Workflow;
using Serilog;

// PostgreSQL (Npgsql): DateTime alanlarını 'timestamp without time zone' olarak yaz.
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

// --- Serilog (Spec Bölüm 11): Elasticsearch'e correlation ID = Robot/JobRun ile loglar ---
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true)
        .Build())
    .CreateLogger();

try
{
    Log.Information("RPA Agent başlatılıyor.");

    var builder = Host.CreateApplicationBuilder(args);

#if DEBUG
    // DEBUG derlemede user-secrets'ı ortamdan bağımsız yükle. Host.CreateApplicationBuilder
    // bunu yalnızca ortam "Development" iken ekler; konsol Agent varsayılan olarak "Production"
    // ortamında koştuğu için aksi halde gerçek DB connection string'i (user-secrets'taki)
    // yüklenmez ve appsettings'teki localhost fallback'ine düşerek bağlantı reddi alınır.
    builder.Configuration.AddUserSecrets<Program>(optional: true);
#endif

    // Windows Service olarak çalıştırılabilir (Unattended). Konsol/tray için de çalışır.
    builder.Services.AddWindowsService(o => o.ServiceName = "RPA.Agent");

    builder.Services.AddSerilog();

    // --- Infrastructure bağımlılıkları (paylaşılan DB üzerinden Orchestrator ile iletişim) ---
    builder.Services.AddDbContext<RpaDbContext>(options =>
        options.UseNpgsql(
            builder.Configuration.GetConnectionString("DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=rpa_dev;Username=postgres;Password=postgres;"));

    builder.Services.AddVaultServices(builder.Configuration); // Orchestrator/Vault kimlik bilgileri.
    builder.Services.AddWorkflowServices();                   // IWorkflowRunner (BaseRunner).
    builder.Services.AddSapGuiChannel();                      // Sap.Gui.* — gerçek SAP GUI Scripting (COM).
    builder.Services.AddRobotServices();                      // IRobotService (kayıt + heartbeat).
    builder.Services.AddQueueServices();                      // IQueueService (iş çekme).

    // --- Ajan çekirdeği: kayıt + heartbeat + yoklama döngüleri + tray ---
    builder.Services.AddAgentCore(builder.Configuration);

    var host = builder.Build();

    // Aktivasyon modu: kodu tuket, credential'i korumali depoya yaz, cik. Ajan dongusu baslamaz —
    // aktivasyon operatorun bir kereye mahsus yaptigi kurulum adimidir, servis calismasi degil.
    string? activationCode = null;
    try
    {
        if (AgentCommandLine.TryGetActivationCode(args, out var code)) activationCode = code;
    }
    catch (ArgumentException ex)
    {
        // Operator hatasi — "beklenmeyen hata" degil: yigin izi basmadan kullanim yaz.
        Log.Error("{Message}", ex.Message);
        return 1;
    }

    if (activationCode is not null)
    {
        return await ActivateAgentAsync(host, activationCode);
    }

    await host.RunAsync();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "RPA Agent beklenmeyen hata ile sonlandı.");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>
/// Aktivasyon kodunu Orchestrator'a sunar; donen credential DPAPI korumali dosyaya yazilir
/// (AgentEnrollmentClient). Kod tek kullanimliktir — basarisiz denemede Studio'dan yeni kod alinir.
/// GUVENLIK: kod ve credential hicbir log yoluna yazilmaz; yalnizca sunucunun stabil hata kodu.
/// </summary>
static async Task<int> ActivateAgentAsync(IHost host, string activationCode)
{
    var options = host.Services.GetRequiredService<IOptions<AgentOptions>>().Value;

    // Aktivasyon istegi AgentId + InstallationId tasir: ikisi de Studio'dan gelir ve
    // yapilandirmada bulunmak ZORUNDADIR — aksi halde sunucuya bos kimlikle gidilirdi.
    if (options.AgentId == Guid.Empty || string.IsNullOrWhiteSpace(options.InstallationId))
    {
        Log.Error(
            "Aktivasyon icin Agent:AgentId ve Agent:InstallationId yapilandirilmalidir. " +
            "AgentId Studio'da agent olusturulunca, InstallationId lisans ekraninda gorunur.");
        return 1;
    }

    try
    {
        var enrollment = host.Services.GetRequiredService<AgentEnrollmentClient>();
        await enrollment.ActivateAsync(activationCode);
        Log.Information(
            "Agent {AgentId} aktive edildi ({MachineName}). Credential korumali depoya yazildi; " +
            "ajan bundan sonra normal baslatilabilir.",
            options.AgentId, options.EffectiveMachineName);
        return 0;
    }
    catch (Exception ex)
    {
        Log.Error("Agent aktivasyonu basarisiz: {Reason}", ex.Message);
        return 1;
    }
}

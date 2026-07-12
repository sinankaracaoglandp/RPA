using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RPA.Agent;
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

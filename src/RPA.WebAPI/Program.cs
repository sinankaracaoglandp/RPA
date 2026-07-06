using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using RPA.Infrastructure.Alerting;
using RPA.Infrastructure.Authentication;
using RPA.Infrastructure.Logging;
using RPA.Infrastructure.Persistence;
using RPA.Infrastructure.Queues;
using RPA.Infrastructure.Robots;
using RPA.Infrastructure.Scheduling;
using RPA.Infrastructure.Vault;
using RPA.Infrastructure.Workflow;
using RPA.WebAPI.Robots;
using RPA.WebAPI.Hubs;
using RPA.WebAPI.Middleware;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// --- Serilog Configuration (Spec Bölüm 11) ---
// Configure Serilog from appsettings.json and register the CorrelationIdEnricher.
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.With<CorrelationIdEnricher>()
    .CreateLogger();

builder.Host.UseSerilog(Log.Logger);

// --- Servisler ---
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// AD/LDAP + JWT kimlik doğrulama servisleri (Spec Bölüm 10).
builder.Services.AddRpaAuthentication(builder.Configuration);

// Credential Vault (HashiCorp / DPAPI) — Spec Bölüm 5.5, 10.
builder.Services.AddVaultServices(builder.Configuration);

// Persistence: RpaDbContext (kuyruk/idempotency repository'lerinin bağımlılığı) — Spec Bölüm 4.
// AddWorkflowServices IQueueItemRepository/IIdempotencyService kaydeder; bunlar RpaDbContext'e muhtaç.
builder.Services.AddDbContext<RpaDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Server=(localdb)\\mssqllocaldb;Database=RPA_Dev;Trusted_Connection=true;"));

// Workflow çekirdeği: aktivite kataloğu + JSON şema doğrulayıcı — Spec Bölüm 5.1, 5.3.
builder.Services.AddWorkflowServices();

// Robot kayıt + heartbeat + offline tespiti servisleri — Spec Bölüm 5.6, 9.
builder.Services.AddRobotServices();

// Kuyruk motoru (UPDLOCK atama + retry) — Task 3.2, Spec Bölüm 7.
builder.Services.AddQueueServices();

// Zamanlayıcı + tetikleyiciler (cron/manuel, OverlapPolicy) — Task 3.3, Spec Bölüm 7.
builder.Services.AddSchedulerServices();

// Orchestrator UI read-side sorguları (İşler/Dashboard) — WP-6.1.
builder.Services.AddScoped<RPA.Domain.Interfaces.IJobRunQueryRepository,
    RPA.Infrastructure.Persistence.EfJobRunQueryRepository>();

// Action Center servisi (bekleyen kayıt listeleme/atama/çözümleme) — WP-6.2.
builder.Services.AddScoped<RPA.Domain.Interfaces.IActionItemRepository,
    RPA.Infrastructure.Persistence.EfActionItemRepository>();
builder.Services.AddScoped<RPA.Infrastructure.ActionCenter.ActionCenterService>();

// Alarm motoru: kural değerlendirme + bildirim (e-posta/Teams) + arka plan servisi — WP-6.3.
builder.Services.AddAlertingServices(builder.Configuration);

// Ortam yönetimi + workflow deployment governance (Dev/Test/Prod, publish/approve) — WP-6.4.
builder.Services.AddScoped<RPA.Domain.Interfaces.IEnvironmentRepository,
    RPA.Infrastructure.Persistence.EfEnvironmentRepository>();
builder.Services.AddScoped<RPA.Domain.Interfaces.IWorkflowVersionRepository,
    RPA.Infrastructure.Persistence.EfWorkflowVersionRepository>();
builder.Services.AddScoped<RPA.Infrastructure.Services.EnvironmentService>();
builder.Services.AddScoped<RPA.Infrastructure.Services.WorkflowDeploymentService>();

// SignalR: robot ajanları ile çift yönlü mesajlaşma (RobotHub).
builder.Services.AddSignalR();

// JWT Bearer authentication middleware.
var jwt = builder.Configuration
    .GetSection(AuthenticationOptions.SectionName)
    .Get<AuthenticationOptions>()?.Jwt ?? new JwtOptions();

// CRITICAL FIX: No fallback to all-zero key. Require JWT_SECRET environment variable.
if (string.IsNullOrEmpty(jwt.Secret))
{
    throw new InvalidOperationException(
        "JWT secret not configured. Set Authentication:Jwt:Secret in appsettings or JWT_SECRET environment variable. " +
        "Secret must be at least 32 bytes (characters).");
}

// HIGH FIX: Derive signing key using PBKDF2 to ensure strength against low-entropy secrets
var derivedKey = DeriveKeyFromSecret(jwt.Secret);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(derivedKey),
            ClockSkew = TimeSpan.FromMinutes(1),
        };

        // SignalR: WebSocket handshake Authorization header taşıyamadığından,
        // hub yolları için access_token query parametresinden JWT okunur (Spec Bölüm 9).
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// CORS: SPA (Angular) kaynağına izin ver.
const string CorsPolicy = "RpaCors";
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins").Get<string[]>() ?? new[] { "http://localhost:4200" };

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicy, policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

// --- Middleware pipeline ---
// CorrelationIdMiddleware must be early (before other middlewares that might log)
app.UseMiddleware<CorrelationIdMiddleware>();

app.UseCors(CorsPolicy);
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => "RPA Platform API");
app.MapControllers();
app.MapHub<RobotHub>("/hubs/robot");
app.MapHub<StudioHub>("/hubs/studio");

app.Run();

// Helper method to derive a cryptographically strong key from JWT secret using PBKDF2.
static byte[] DeriveKeyFromSecret(string secret)
{
    // Use PBKDF2 to derive a 32-byte key from the secret.
    // This strengthens the key against low-entropy input and provides proper key derivation.
    var secretBytes = Encoding.UTF8.GetBytes(secret);
    var saltBytes = Encoding.UTF8.GetBytes("RPA.JwtTokenService.v1"); // Fixed salt for consistency

    // Use static Pbkdf2 method (not deprecated constructor)
    return Rfc2898DeriveBytes.Pbkdf2(
        secretBytes,
        saltBytes,
        iterations: 10000,
        HashAlgorithmName.SHA256,
        outputLength: 32);
}

// Integration test (WebApplicationFactory) için erişilebilir Program sınıfı.
public partial class Program { }

using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using RPA.Infrastructure.Authentication;
using RPA.Infrastructure.Logging;
using RPA.Infrastructure.Vault;
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

// JWT Bearer authentication middleware.
var jwt = builder.Configuration
    .GetSection(AuthenticationOptions.SectionName)
    .Get<AuthenticationOptions>()?.Jwt ?? new JwtOptions();

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
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(string.IsNullOrEmpty(jwt.Secret)
                    ? new string('0', 32)
                    : jwt.Secret)),
            ClockSkew = TimeSpan.FromMinutes(1),
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

app.Run();

// Integration test (WebApplicationFactory) için erişilebilir Program sınıfı.
public partial class Program { }

namespace RPA.Infrastructure.Audit;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RPA.Domain.Interfaces;

/// <summary>
/// AuditLog altyapısının DI kaydı (Spec Bölüm 11).
/// </summary>
public static class AuditServiceCollectionExtensions
{
    public static IServiceCollection AddRpaAudit(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AuditOptions>(
            configuration.GetSection(AuditOptions.SectionName));

        var enabled = configuration.GetSection(AuditOptions.SectionName)
            .GetValue<bool?>(nameof(AuditOptions.Enabled)) ?? true;

        services.AddSingleton(new AuditInterceptor(enabled));
        services.AddScoped<IAuditService, AuditService>();

        return services;
    }
}

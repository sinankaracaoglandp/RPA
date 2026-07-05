namespace RPA.Infrastructure.Scheduling;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RPA.Domain.Interfaces;

/// <summary>Zamanlayıcı + tetikleyiciler DI kaydı (Task 3.3, Spec Bölüm 7).</summary>
public static class SchedulerServiceCollectionExtensions
{
    public static IServiceCollection AddSchedulerServices(this IServiceCollection services)
    {
        services.AddScoped<ITriggerRepository, Persistence.EfTriggerRepository>();
        services.AddScoped<ITriggerService, TriggerService>();
        services.AddHostedService<SchedulerHostedService>();
        return services;
    }
}

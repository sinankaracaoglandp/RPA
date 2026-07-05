namespace RPA.Infrastructure.Robots;

using Microsoft.Extensions.DependencyInjection;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Persistence;

/// <summary>Robot servisi DI kaydı (Task 3.1, Spec Bölüm 5.6, 9).</summary>
public static class RobotServiceCollectionExtensions
{
    public static IServiceCollection AddRobotServices(this IServiceCollection services)
    {
        services.AddScoped<IRobotRepository, EfRobotRepository>();
        services.AddScoped<IRobotService, RobotService>();
        return services;
    }
}

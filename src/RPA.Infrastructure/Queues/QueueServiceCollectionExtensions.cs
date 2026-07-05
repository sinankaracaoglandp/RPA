namespace RPA.Infrastructure.Queues;

using Microsoft.Extensions.DependencyInjection;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Persistence;

/// <summary>Kuyruk motoru DI kaydı (Task 3.2, Spec Bölüm 7).</summary>
public static class QueueServiceCollectionExtensions
{
    public static IServiceCollection AddQueueServices(this IServiceCollection services)
    {
        services.AddScoped<IQueueItemRepository, EfQueueItemRepository>();
        services.AddScoped<IQueueService, QueueService>();
        return services;
    }
}

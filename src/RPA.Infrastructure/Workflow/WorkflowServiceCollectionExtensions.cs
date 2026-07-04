namespace RPA.Infrastructure.Workflow;

using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Workflow çekirdek servislerinin DI kaydı: aktivite kataloğu ve şema doğrulayıcı.
/// Spec Bölüm 5.1, 5.3.
/// </summary>
public static class WorkflowServiceCollectionExtensions
{
    public static IServiceCollection AddWorkflowServices(this IServiceCollection services)
    {
        // Katalog tüm platformda paylaşılan sabit referans → singleton.
        services.AddSingleton<ActivityCatalog>();

        // Doğrulayıcı stateless (şema statik cache'li) → transient yeterli.
        services.AddTransient<WorkflowValidator>();

        return services;
    }
}

namespace RPA.Infrastructure.Workflow;

using Microsoft.Extensions.DependencyInjection;
using RPA.Domain.Interfaces;

/// <summary>
/// Workflow çekirdek servislerinin DI kaydı: aktivite kataloğu, şema doğrulayıcı ve BaseRunner.
/// Spec Bölüm 5.1, 5.2, 5.3.
/// </summary>
public static class WorkflowServiceCollectionExtensions
{
    public static IServiceCollection AddWorkflowServices(this IServiceCollection services)
    {
        // Katalog tüm platformda paylaşılan sabit referans → singleton.
        services.AddSingleton<ActivityCatalog>();

        // Doğrulayıcı stateless (şema statik cache'li) → transient yeterli.
        services.AddTransient<WorkflowValidator>();

        // Aktivite implementasyonları Faz 2.6–2.9'da eklenene dek boş factory.
        // (İçerik eklendiğinde bu kayıt gerçek factory ile değiştirilir.)
        services.AddSingleton<IActivityFactory, EmptyActivityFactory>();

        // Her workflow çalıştırması için ayrı runner (state machine, çalışma başına scope).
        services.AddTransient<IWorkflowRunner, BaseRunner>();

        return services;
    }
}

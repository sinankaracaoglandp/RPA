namespace RPA.Infrastructure.SAP;

using Microsoft.Extensions.DependencyInjection;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.SAP.SapGuiElements;

/// <summary>
/// SAP GUI Scripting (fallback) kanal servislerinin DI kaydı (Task 4.1). Spec Bölüm 6.
/// </summary>
public static class SapGuiServiceCollectionExtensions
{
    public static IServiceCollection AddSapGuiChannel(this IServiceCollection services)
    {
        // Oturum yöneticisi süreç genelinde tekil; kanal her JobRun için ayrı (scoped).
        services.AddSingleton<ISapGuiSessionManager, SapGuiSessionManager>();
        services.AddScoped<ISapGuiChannel, SapGuiChannel>();

        // Aktiviteler
        services.AddTransient<SapGuiConnectActivity>();
        services.AddTransient<SapGuiClickActivity>();
        services.AddTransient<SapGuiSetTextActivity>();
        services.AddTransient<SapGuiGetTextActivity>();
        services.AddTransient<SapGuiSelectTabActivity>();
        services.AddTransient<SapGuiGridReadActivity>();
        services.AddTransient<SapGuiScreenshotActivity>();

        return services;
    }
}

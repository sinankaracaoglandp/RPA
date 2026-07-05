namespace RPA.Infrastructure.SAP.Nco;

using Microsoft.Extensions.DependencyInjection;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.SAP;

/// <summary>
/// SAP NCo (birincil, programatik) kanal servislerinin DI kaydı (Task 4.2). Spec Bölüm 6.
///
/// Not: <see cref="SapConnectionPool"/> ve <see cref="IRfcConnectionFactory"/> uygulamaya özgü
/// bağlantı ayarları gerektirdiğinden (Vault'tan çözülen kimlik bilgileri) burada kaydedilmez;
/// çağıran, ilgili SapConnection için havuzu ayrıca oluşturur/kaydeder. Bu metot kanal ve
/// aktiviteleri kaydeder.
/// </summary>
public static class SapNcoServiceCollectionExtensions
{
    public static IServiceCollection AddSapNcoActivities(this IServiceCollection services)
    {
        services.AddScoped<ISapDataChannel, SapNcoChannel>();

        services.AddTransient<SapNcoCallBapiActivity>();
        services.AddTransient<SapNcoReadTableActivity>();
        services.AddTransient<SapNcoCommitActivity>();

        return services;
    }
}

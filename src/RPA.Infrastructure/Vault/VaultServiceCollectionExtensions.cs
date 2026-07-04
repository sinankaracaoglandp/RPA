namespace RPA.Infrastructure.Vault;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RPA.Domain.Interfaces;

/// <summary>
/// Credential Vault servislerinin DI kaydı.
/// Spec Bölüm 5.5, 10 — HashiCorp Vault ya da DPAPI, config'e göre seçilir.
/// </summary>
public static class VaultServiceCollectionExtensions
{
    public static IServiceCollection AddVaultServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(VaultOptions.SectionName);
        services.Configure<VaultOptions>(section);

        var type = section.GetValue<string>("Type") ?? "Dpapi";

        if (string.Equals(type, "HashiCorp", StringComparison.OrdinalIgnoreCase))
        {
            // Typed HttpClient — BaseAddress/token client ctor'unda ayarlanır.
            services.AddHttpClient<ICredentialVault, HashiCorpVaultClient>();
        }
        else
        {
            services.AddSingleton<ICredentialVault, DpapiVaultImpl>();
        }

        return services;
    }
}

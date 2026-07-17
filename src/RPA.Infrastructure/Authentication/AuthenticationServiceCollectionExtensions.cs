namespace RPA.Infrastructure.Authentication;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RPA.Domain.Interfaces;

/// <summary>
/// AD/LDAP + JWT kimlik doğrulama servislerinin DI kaydı.
/// </summary>
public static class AuthenticationServiceCollectionExtensions
{
    /// <param name="useFakeLdap">
    /// Debug/Development'ta true verilir: gerçek AD'ye bind atmayan
    /// <see cref="DevFakeLdapConnector"/> kaydedilir; böylece hatalı giriş denemeleri
    /// domain hesabını kilitlemez. Production'da (Release) false kalmalı.
    /// </param>
    public static IServiceCollection AddRpaAuthentication(
        this IServiceCollection services, IConfiguration configuration, bool useFakeLdap = false)
    {
        services.Configure<AuthenticationOptions>(
            configuration.GetSection(AuthenticationOptions.SectionName));

        if (useFakeLdap)
        {
            services.AddScoped<ILdapConnector, DevFakeLdapConnector>();
        }
        else
        {
            services.AddScoped<ILdapConnector, LdapForNetConnector>();
        }

        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddScoped<AgentTokenService>();
        services.AddScoped<IAuthenticationService, LdapAuthService>();

        return services;
    }
}

namespace RPA.Infrastructure.Authentication;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RPA.Domain.Interfaces;

/// <summary>
/// AD/LDAP + JWT kimlik doğrulama servislerinin DI kaydı.
/// </summary>
public static class AuthenticationServiceCollectionExtensions
{
    public static IServiceCollection AddRpaAuthentication(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AuthenticationOptions>(
            configuration.GetSection(AuthenticationOptions.SectionName));

        services.AddScoped<ILdapConnector, LdapForNetConnector>();
        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddScoped<IAuthenticationService, LdapAuthService>();

        return services;
    }
}

namespace RPA.Infrastructure.Alerting;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Persistence;

/// <summary>Alarm motoru DI kaydı (WP-6.3).</summary>
public static class AlertingServiceCollectionExtensions
{
    public static IServiceCollection AddAlertingServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        var engineOptions = new AlertEngineOptions();
        configuration.GetSection("Alerting").Bind(engineOptions);
        services.AddSingleton(engineOptions);

        var emailOptions = new AlertEmailOptions();
        configuration.GetSection("Alerting:Smtp").Bind(emailOptions);
        services.AddSingleton(emailOptions);

        services.AddScoped<IAlertRuleRepository, EfAlertRuleRepository>();
        services.AddScoped<IAlertMetricsProvider, AlertMetricsProvider>();
        services.AddSingleton<AlertConditionEvaluator>();
        services.AddScoped<IAlertEmailSender, SmtpAlertEmailSender>();
        services.AddHttpClient<INotificationSender, ChannelNotificationSender>();
        services.AddScoped<AlertEvaluationService>();

        services.AddHostedService<AlertEvaluationHostedService>();

        return services;
    }
}

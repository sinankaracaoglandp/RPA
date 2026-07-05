namespace RPA.Agent;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RPA.Agent.Configuration;
using RPA.Agent.Hosting;
using RPA.Agent.Jobs;
using RPA.Agent.Registration;
using RPA.Agent.Session;
using RPA.Agent.State;
using RPA.Agent.Tray;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Retry;

/// <summary>
/// Ajan servislerini DI konteynerine kaydeder. Hosted service sırası önemlidir:
/// önce kayıt (RegistrationHostedService), sonra heartbeat ve yoklama döngüleri.
/// </summary>
public static class AgentServiceCollectionExtensions
{
    public static IServiceCollection AddAgentCore(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<AgentOptions>()
            .Bind(configuration.GetSection(AgentOptions.SectionName))
            .ValidateOnStart();

        // Paylaşılan çalışma zamanı durumu (tek örnek).
        services.AddSingleton<IAgentState, AgentState>();

        // İstisna sınıflandırıcı (Business/System) — iş sonucu raporlama için.
        services.AddSingleton<ExceptionClassifier>();

        // Kayıt + iş çalıştırma + iş kaynağı — scoped bağımlılıklar (IRobotService/IQueueService/
        // IWorkflowRunner) taşıdıkları için scoped; hosted service'ler bunları scope içinde çözer.
        services.AddScoped<IRobotRegistrar, RobotRegistrar>();
        services.AddScoped<JobExecutor>();
        services.AddScoped<IAgentJobSource, QueueAgentJobSource>();

        // Tray sunucusu (attended mod).
        services.AddSingleton<TrayStatusPresenter>();

        // Oturum yönetimi (RDP/AutoLogon/tscon — Spec Bölüm 9).
        services.AddOptions<SessionManagerOptions>()
            .Bind(configuration.GetSection(SessionManagerOptions.SectionName));
        services.AddSingleton<SessionCredentialProvider>();
        services.AddSingleton<ISessionManager, WindowsSessionManager>();
        if (OperatingSystem.IsWindows())
        {
            // Gerçek Windows interop implementasyonları yalnızca Windows'ta bağlanır.
            services.AddSingleton<IAutoLogonRegistry, WinlogonAutoLogonRegistry>();
            services.AddSingleton<ISessionSwitcher, TsconSessionSwitcher>();
            services.AddSingleton<ISessionInfoProvider, WtsSessionInfoProvider>();
        }

        // Hosted service sırası: kayıt önce.
        services.AddHostedService<RegistrationHostedService>();
        services.AddHostedService<HeartbeatBackgroundService>();
        services.AddHostedService<QueuePollingBackgroundService>();

        return services;
    }
}

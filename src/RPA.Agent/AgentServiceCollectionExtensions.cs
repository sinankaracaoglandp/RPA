namespace RPA.Agent;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RPA.Agent.Configuration;
using RPA.Agent.Hosting;
using RPA.Agent.Hub;
using RPA.Agent.JobList;
using RPA.Agent.Jobs;
using RPA.Agent.Prompts;
using RPA.Agent.Registration;
using RPA.Agent.Session;
using RPA.Agent.State;
using RPA.Agent.Tray;
using RPA.Agent.UISpy;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Retry;
using RPA.Infrastructure.UISpy;

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

        // Kimlik doğrulama (Task 5): korumalı credential deposu + paylaşılan erişim tokeni sağlayıcısı.
        // Credential yalnızca DPAPI LocalMachine ile korunan dosyada tutulur (Windows dışı ortamlarda —
        // ör. testler — depo ayrıca kaydedilmez; sağlayıcı arayüz üzerinden çalışır).
        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<Authentication.IAgentCredentialStore, Authentication.DpapiAgentCredentialStore>();
        }

        services.AddHttpClient<Authentication.AgentEnrollmentClient>();
        services.AddSingleton<Authentication.IAgentTokenClient>(sp =>
            sp.GetRequiredService<Authentication.AgentEnrollmentClient>());
        services.AddSingleton<Authentication.IAgentAccessTokenProvider, Authentication.AgentAccessTokenProvider>();

        // Tüm SignalR hub bağlantılarının tek üretim noktası (orkestratör URL'i + ajan JWT'si).
        services.AddSingleton<Authentication.IAgentHubConnectionFactory, Authentication.AgentHubConnectionFactory>();

        // İstisna sınıflandırıcı (Business/System) — iş sonucu raporlama için.
        services.AddSingleton<ExceptionClassifier>();

        // Kayıt + iş çalıştırma + iş kaynağı — scoped bağımlılıklar (IRobotService/IQueueService/
        // IWorkflowRunner) taşıdıkları için scoped; hosted service'ler bunları scope içinde çözer.
        services.AddScoped<IRobotRegistrar, RobotRegistrar>();
        services.AddScoped<JobExecutor>();
        services.AddScoped<IAgentJobSource, QueueAgentJobSource>();

        // Tray sunucusu (attended mod).
        services.AddSingleton<TrayStatusPresenter>();

        // Attended UX (Task 3.6 — tray, iş listesi, UserPrompt): gerçek zamanlı iş listesi modeli,
        // kullanıcı girdi kanalı, SignalR bağlantı durumu ve iş olayı yönlendirme — tümü tekil (uygulama
        // ömrü boyunca tek örnek; tray/pencereler bu örnekleri paylaşır).
        services.AddSingleton<JobListViewModel>();
        services.AddSingleton<UserPromptService>();
        services.AddSingleton<IUserPromptChannel>(sp => sp.GetRequiredService<UserPromptService>());
        services.AddSingleton<HubConnectionStatusCoordinator>();
        services.AddSingleton<JobEventRouter>();
        services.AddSingleton<IJobHubClient, RobotHubClient>();

        // Canlı çalıştırma konsolu: BaseRunner node olaylarını RobotHub üzerinden Studio'ya iletir.
        // BaseRunner (transient) opsiyonel IWorkflowExecutionObserver parametresini DI'dan çözer.
        services.AddSingleton<RPA.Domain.Interfaces.IWorkflowExecutionObserver, Jobs.AgentWorkflowExecutionObserver>();

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
        services.AddHostedService<JobHubHostedService>();
        services.AddHostedService<TrayUiHostedService>();

        // UI Spy (Task 4.4, Spec Bölüm 6): gerçek zamanlı SAP GUI element tespiti — Studio tasarımcısının
        // point-and-click ile element seçmesini sağlar. Güvenlik: yalnızca ATTENDED modda + Windows'ta.
        var agentOptions = configuration.GetSection(AgentOptions.SectionName).Get<AgentOptions>() ?? new AgentOptions();
        if (agentOptions.Mode == RobotMode.Attended && OperatingSystem.IsWindows())
        {
            services.AddOptions<UiSpyOptions>()
                .Bind(configuration.GetSection(UiSpyOptions.SectionName));

            // Native P/Invoke + SAP COM resolver (stub — SAP GUI kurulu makinede gerçek COM resolver ile değişir).
            services.AddSingleton<INativeWindowApi, Win32NativeWindowApi>();
            services.AddSingleton<ISapGuiElementResolver, NullSapGuiElementResolver>();
            services.AddSingleton<SapGuiElementDetector>();

            // SignalR köprüsü (agent → Studio) + gönderici + orkestrasyon servisi.
            services.AddSingleton<ISpyElementTransport, SignalRSpyElementTransport>();
            services.AddSingleton<SapGuiElementSender>();
            services.AddSingleton<SapGuiSpyService>();
            services.AddOptions<SpySessionOptions>()
                .Bind(configuration.GetSection(SpySessionOptions.SectionName));
            services.AddSingleton<ISapGuiSinglePicker, SapGuiSinglePicker>();
            // Paket E: DesktopSpy tek-seçim picker'ı (FlaUI/UIA). Koordinatör kind:"desktop" için kullanır.
            services.AddSingleton<IDesktopSinglePicker, Desktop.FlaUiDesktopSinglePicker>();
            // Paket D: WebSpy tek-seçim picker'ı (Playwright/DOM). Koordinatör kind:"web" için kullanır.
            services.AddSingleton<IWebSinglePicker, PlaywrightWebSinglePicker>();
            // Paket F: image bölge picker'ı. Koordinatör kind:"image" için kullanır.
            services.AddSingleton<IImageRegionPicker, GdiImageRegionPicker>();
            // Text-offset picker: capa+ofset secimi. Koordinator kind:"text-offset" icin kullanir.
            services.AddSingleton<ITextOffsetPicker, GdiTextOffsetPicker>();
            services.AddSingleton<ISpySessionCoordinator, SpySessionCoordinator>();
            services.AddSingleton<ISpyCommandConnection, SignalRSpyCommandConnection>();

            services.AddHostedService<UiSpyHostedService>();
            services.AddHostedService<SpyHubCommandHostedService>();
        }

        // Paket E: Windows Masaüstü otomasyon kanalı (FlaUI/UIA3). Desktop.* aktiviteleri
        // bu kanalı çalıştırma anında kullanır. Yalnız Windows'ta kayıtlıdır.
        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<IDesktopAutomationChannel, Desktop.FlaUiDesktopAutomationChannel>();
            // Paket F: Görüntü/OCR fallback otomasyon kanalı (OpenCvSharp4 + Tesseract). Infrastructure'daki
            // UnavailableVisionAutomationChannel (TryAddSingleton) kaydını Windows'ta gerçek impl ile değiştirir.
            services.AddSingleton<RPA.Domain.Interfaces.IVisionAutomationChannel, Vision.TesseractOpenCvVisionChannel>();
        }

        return services;
    }
}

namespace RPA.Agent.Hosting;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RPA.Agent.Configuration;
using RPA.Agent.Registration;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;

/// <summary>
/// Ajan başlangıcında robotu Orchestrator'a kaydeder. StartAsync içinde (awaited) çalıştığı için
/// heartbeat ve yoklama servisleri başlamadan önce robot kimliği garanti altına alınır
/// (IHostedService'ler kayıt sırasıyla başlatılır — bu servis DI'de ilk sırada olmalıdır).
/// IRobotRegistrar scoped bağımlılıklara (IRobotService) sahip olduğundan bir scope içinde çözülür.
/// </summary>
public sealed class RegistrationHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISessionManager _sessionManager;
    private readonly AgentOptions _options;
    private readonly ILogger<RegistrationHostedService> _logger;

    public RegistrationHostedService(
        IServiceScopeFactory scopeFactory,
        ISessionManager sessionManager,
        IOptions<AgentOptions> options,
        ILogger<RegistrationHostedService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Ajan başlatılıyor — Orchestrator'a kayıt yapılıyor.");
        using var scope = _scopeFactory.CreateScope();
        var registrar = scope.ServiceProvider.GetRequiredService<IRobotRegistrar>();
        await registrar.RegisterAsync(cancellationToken);

        // Kayıttan sonra oturum garanti edilir (Spec Bölüm 9). AutoLogon/tscon hataları
        // ajanın başlamasını engellememelidir; loglanır ve devam edilir.
        var sessionMode = _options.Mode == RobotMode.Attended ? SessionMode.Attended : SessionMode.Unattended;
        try
        {
            await _sessionManager.EnsureSessionAsync(sessionMode, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Oturum kurulumu ({Mode}) başarısız — ajan kayıtla devam ediyor.", sessionMode);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

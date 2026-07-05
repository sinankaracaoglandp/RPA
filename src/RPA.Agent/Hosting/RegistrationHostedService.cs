namespace RPA.Agent.Hosting;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RPA.Agent.Registration;

/// <summary>
/// Ajan başlangıcında robotu Orchestrator'a kaydeder. StartAsync içinde (awaited) çalıştığı için
/// heartbeat ve yoklama servisleri başlamadan önce robot kimliği garanti altına alınır
/// (IHostedService'ler kayıt sırasıyla başlatılır — bu servis DI'de ilk sırada olmalıdır).
/// IRobotRegistrar scoped bağımlılıklara (IRobotService) sahip olduğundan bir scope içinde çözülür.
/// </summary>
public sealed class RegistrationHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RegistrationHostedService> _logger;

    public RegistrationHostedService(IServiceScopeFactory scopeFactory, ILogger<RegistrationHostedService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Ajan başlatılıyor — Orchestrator'a kayıt yapılıyor.");
        using var scope = _scopeFactory.CreateScope();
        var registrar = scope.ServiceProvider.GetRequiredService<IRobotRegistrar>();
        await registrar.RegisterAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

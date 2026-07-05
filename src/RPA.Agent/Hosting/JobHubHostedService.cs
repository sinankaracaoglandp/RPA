namespace RPA.Agent.Hosting;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RPA.Agent.Hub;
using RPA.Agent.State;
using RPA.Agent.Tray;

/// <summary>
/// RobotHub SignalR istemcisini ajan kaydından sonra başlatır (Task 3.6). Bağlantı durumu
/// değişikliklerini tray sunucusuna (<see cref="TrayStatusPresenter"/>) yansıtır — Offline/Reconnecting
/// tray tooltip'inde görünür (Spec Bölüm 9). RobotId atanana kadar bekler.
/// </summary>
public sealed class JobHubHostedService : BackgroundService
{
    private readonly IJobHubClient _hubClient;
    private readonly HubConnectionStatusCoordinator _statusCoordinator;
    private readonly TrayStatusPresenter _trayPresenter;
    private readonly IAgentState _state;
    private readonly ILogger<JobHubHostedService> _logger;

    public JobHubHostedService(
        IJobHubClient hubClient,
        HubConnectionStatusCoordinator statusCoordinator,
        TrayStatusPresenter trayPresenter,
        IAgentState state,
        ILogger<JobHubHostedService> logger)
    {
        _hubClient = hubClient ?? throw new ArgumentNullException(nameof(hubClient));
        _statusCoordinator = statusCoordinator ?? throw new ArgumentNullException(nameof(statusCoordinator));
        _trayPresenter = trayPresenter ?? throw new ArgumentNullException(nameof(trayPresenter));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _statusCoordinator.StatusChanged += _trayPresenter.UpdateConnectionStatus;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // RobotRegistrar kaydı tamamlayana kadar bekle (RegistrationHostedService önce çalışır).
        try
        {
            while (_state.RobotId is null && !stoppingToken.IsCancellationRequested)
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (stoppingToken.IsCancellationRequested || _state.RobotId is null)
            return;

        try
        {
            await _hubClient.StartAsync(_state.RobotId.Value, stoppingToken);
            _logger.LogInformation("RobotHub SignalR bağlantısı başlatıldı.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RobotHub SignalR bağlantısı başlatılamadı — çevrimdışı modda devam edilecek.");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await _hubClient.StopAsync(cancellationToken);
    }
}

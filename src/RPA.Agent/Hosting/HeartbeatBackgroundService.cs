namespace RPA.Agent.Hosting;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RPA.Agent.Configuration;
using RPA.Agent.Connectivity;
using RPA.Agent.State;
using RPA.Domain.Interfaces;

/// <summary>
/// Heartbeat döngüsü (Spec Bölüm 9): her <see cref="AgentOptions.HeartbeatInterval"/> (varsayılan 30 sn)
/// robotun canlılığını Orchestrator'a bildirir. 5 dk boyunca heartbeat gelmezse Orchestrator robotu
/// offline işaretler. Tek bir heartbeat hatası döngüyü durdurmaz — bir sonraki turda yeniden denenir.
/// IRobotService scoped olduğundan her heartbeat bir scope içinde çözülür.
/// </summary>
public sealed class HeartbeatBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAgentState _state;
    private readonly AgentOptions _options;
    private readonly ILogger<HeartbeatBackgroundService> _logger;
    private readonly ConnectivityLease? _lease;

    public HeartbeatBackgroundService(
        IServiceScopeFactory scopeFactory,
        IAgentState state,
        IOptions<AgentOptions> options,
        ILogger<HeartbeatBackgroundService> logger,
        ConnectivityLease? lease = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _lease = lease;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await SendHeartbeatAsync(stoppingToken);
            try
            {
                await Task.Delay(_options.HeartbeatInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Tek bir heartbeat gönderir (test edilebilirlik için public). Hata yakalanır.</summary>
    public async Task SendHeartbeatAsync(CancellationToken cancellationToken = default)
    {
        var robotId = _state.RobotId;
        if (robotId is null)
            return; // Henüz kayıt olmadı.

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var robotService = scope.ServiceProvider.GetRequiredService<IRobotService>();
            await robotService.RecordHeartbeatAsync(robotId.Value, cancellationToken);
            _state.RecordHeartbeat(DateTime.UtcNow);

            // Başarılı heartbeat = "son BAŞARILI sunucu doğrulaması" (Task 6 — ConnectivityLease).
            // Kirayı besleyen tek kaynak budur: heartbeat aralığı (varsayılan 30 sn) 15 dakikalık
            // kira sınırından çok küçüktür, dolayısıyla sağlıklı bağlantıda kira hiç dolmaz;
            // orkestratöre ulaşılamadığı andan itibaren 15 dk sayacı işler.
            _lease?.RecordServerValidation();
            _logger.LogDebug("Heartbeat gönderildi — Robot {RobotId}.", robotId);
        }
        catch (Exception ex)
        {
            // Kira SÜRESİ etkilenmez (çalışan node normal sınırına ulaşmalı) — yalnız bağlantı
            // kopuk işaretlenir; süre son başarılı doğrulamadan itibaren işlemeye devam eder.
            _lease?.MarkDisconnected();
            _logger.LogError(ex, "Heartbeat gönderilemedi — Robot {RobotId}. Sonraki turda yeniden denenecek.", robotId);
        }
    }
}

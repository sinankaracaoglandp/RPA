namespace RPA.Agent.Hub;

using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using RPA.Agent.Configuration;
using RPA.Agent.Prompts;

/// <summary>
/// RobotHub'a (Task 3.1 SignalR hub) bağlanan gerçek istemci sarmalayıcısı (Spec Bölüm 9).
/// Otomatik yeniden bağlanma (<see cref="HubConnectionBuilderExtensions.WithAutomaticReconnect"/>)
/// kullanır; yaşam döngüsü olaylarını <see cref="HubConnectionStatusCoordinator"/>'a, iş olaylarını
/// <see cref="JobEventRouter"/>'a, kullanıcı girdi isteklerini <see cref="UserPromptService"/>'e yönlendirir.
/// İnce bir bağlama (wiring) sınıfıdır — asıl mantık test edilen coordinator/router/service sınıflarındadır.
/// </summary>
public sealed class RobotHubClient : IJobHubClient
{
    private readonly HubConnection _connection;
    private readonly HubConnectionStatusCoordinator _statusCoordinator;
    private readonly JobEventRouter _jobEventRouter;
    private readonly UserPromptService _userPromptService;
    private readonly ILogger<RobotHubClient> _logger;

    public RobotHubClient(
        RPA.Agent.Authentication.IAgentHubConnectionFactory connectionFactory,
        HubConnectionStatusCoordinator statusCoordinator,
        JobEventRouter jobEventRouter,
        UserPromptService userPromptService,
        ILogger<RobotHubClient> logger)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _statusCoordinator = statusCoordinator ?? throw new ArgumentNullException(nameof(statusCoordinator));
        _jobEventRouter = jobEventRouter ?? throw new ArgumentNullException(nameof(jobEventRouter));
        _userPromptService = userPromptService ?? throw new ArgumentNullException(nameof(userPromptService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _connection = connectionFactory.Create("/hubs/robot");

        _connection.Reconnecting += error => { _statusCoordinator.OnReconnecting(error); return Task.CompletedTask; };
        _connection.Reconnected += _ => { _statusCoordinator.OnReconnected(); return Task.CompletedTask; };
        _connection.Closed += error => { _statusCoordinator.OnClosed(error); return Task.CompletedTask; };

        _connection.On<JobEventDto>("JobStatusChanged", jobEvent =>
        {
            try { _jobEventRouter.Handle(jobEvent); }
            catch (Exception ex) { _logger.LogError(ex, "JobStatusChanged işlenirken hata."); }
        });

        _connection.On<Guid, string, string, string?>("UserPromptRequested", (requestId, title, message, kind) =>
        {
            // Sunucu tarafında üretilen istek ID'si korunur; UI bu event'i UserPromptService.PromptRaised
            // üzerinden değil, doğrudan requestId eşleşmesiyle görür (RequestAsync sunucu tarafında da
            // çağrılmış olabilir — burada yalnızca bildirim amaçlıdır, gerçek akış çalışan bağlam JobExecutor'dadır).
            _logger.LogInformation("Sunucudan UserPrompt bildirimi alındı {RequestId} ({Title})", requestId, title);
        });
    }

    public async Task StartAsync(Guid robotId, CancellationToken cancellationToken = default)
    {
        await _connection.StartAsync(cancellationToken);
        _statusCoordinator.OnConnected();
        await _connection.InvokeAsync("Heartbeat", robotId, cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _connection.StopAsync(cancellationToken);
        _statusCoordinator.OnClosed(null);
    }

    public async Task ReportNodeLogAsync(
        RPA.Domain.Interfaces.NodeExecutionEvent evt, CancellationToken cancellationToken = default)
    {
        if (_connection.State != HubConnectionState.Connected)
        {
            return; // Bağlantı yoksa canlı log best-effort — sessizce atla.
        }
        await _connection.SendAsync("ReportNodeLog", evt, cancellationToken);
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
}

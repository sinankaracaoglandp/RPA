namespace RPA.Agent.UISpy;

using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RPA.Agent.Configuration;

public interface ISpyCommandConnection : IAsyncDisposable
{
    void OnStartSpy(Func<Guid, string, string?, Task> handler);
    void OnStopSpy(Func<Guid, Task> handler);
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}

public sealed class SignalRSpyCommandConnection : ISpyCommandConnection
{
    private readonly HubConnection _connection;

    public SignalRSpyCommandConnection(IOptions<AgentOptions> options)
    {
        var url = options?.Value?.OrchestratorUrl ?? throw new ArgumentNullException(nameof(options));
        _connection = new HubConnectionBuilder()
            .WithUrl($"{url.TrimEnd('/')}/hubs/studio")
            .WithAutomaticReconnect()
            .Build();
    }

    public void OnStartSpy(Func<Guid, string, string?, Task> handler)
        => _connection.On("StartSpy", handler);

    public void OnStopSpy(Func<Guid, Task> handler)
        => _connection.On("StopSpy", handler);

    public Task StartAsync(CancellationToken cancellationToken = default)
        => _connection.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default)
        => _connection.StopAsync(cancellationToken);

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}

public sealed class SpyHubCommandHostedService : BackgroundService
{
    private readonly ISpyCommandConnection _connection;
    private readonly ISpySessionCoordinator _coordinator;
    private readonly ILogger<SpyHubCommandHostedService> _logger;

    public SpyHubCommandHostedService(
        ISpyCommandConnection connection,
        ISpySessionCoordinator coordinator,
        ILogger<SpyHubCommandHostedService> logger)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _connection.OnStartSpy(async (sessionId, kind, optionsJson) =>
        {
            try
            {
                await _coordinator.StartAsync(sessionId, kind, optionsJson, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "UI Spy StartSpy komutu islenemedi {SessionId} ({Kind}).", sessionId, kind);
            }
        });

        _connection.OnStopSpy(sessionId => _coordinator.StopAsync(sessionId, stoppingToken));

        
        await _connection.StartAsync(stoppingToken);
        _logger.LogInformation("UI Spy Hub komut baglantisi baslatildi.");

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            await _connection.StopAsync(CancellationToken.None);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await _connection.DisposeAsync();
    }
}

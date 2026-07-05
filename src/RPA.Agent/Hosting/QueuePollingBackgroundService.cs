namespace RPA.Agent.Hosting;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RPA.Agent.Configuration;
using RPA.Agent.Jobs;
using RPA.Agent.State;

/// <summary>
/// Ana iş döngüsü (Spec Bölüm 9): her <see cref="AgentOptions.PollInterval"/> (varsayılan 5 sn)
/// kuyruğu yoklar. İş varsa <see cref="JobExecutor"/> ile izole çalıştırır ve sonucu kuyruğa
/// raporlar. Duraklatılmışsa (tray) iş çekmez. İş bulunduğunda hemen bir sonraki işe geçer.
/// Her yoklama turu kendi DI scope'unda çalışır (IQueueService/IWorkflowRunner scoped) — böylece
/// işler arası durum sızıntısı olmaz (iş izolasyonu).
/// </summary>
public sealed class QueuePollingBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAgentState _state;
    private readonly AgentOptions _options;
    private readonly ILogger<QueuePollingBackgroundService> _logger;

    public QueuePollingBackgroundService(
        IServiceScopeFactory scopeFactory,
        IAgentState state,
        IOptions<AgentOptions> options,
        ILogger<QueuePollingBackgroundService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            bool processed = false;
            try
            {
                processed = await PollOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Kuyruk yoklama turunda beklenmeyen hata.");
            }

            if (processed)
                continue; // İş vardı — bekleme, sıradakini al.

            try
            {
                await Task.Delay(_options.PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Bir yoklama turu: iş çeker, izole scope'ta çalıştırır, sonucu raporlar. İş işlendiyse true.
    /// Test edilebilirlik için public.
    /// </summary>
    public async Task<bool> PollOnceAsync(CancellationToken cancellationToken = default)
    {
        if (_state.IsPaused || _state.RobotId is null)
            return false;

        using var scope = _scopeFactory.CreateScope();
        var jobSource = scope.ServiceProvider.GetRequiredService<IAgentJobSource>();
        var executor = scope.ServiceProvider.GetRequiredService<JobExecutor>();

        var job = await jobSource.DequeueAsync(cancellationToken);
        if (job is null)
            return false;

        var outcome = await executor.ExecuteAsync(job, cancellationToken);

        if (outcome.Success)
        {
            await jobSource.ReportSuccessAsync(job.ItemId, cancellationToken);
        }
        else
        {
            await jobSource.ReportFailureAsync(
                job.ItemId, outcome.Exception?.Message, outcome.IsBusinessException, cancellationToken);
        }

        return true;
    }
}

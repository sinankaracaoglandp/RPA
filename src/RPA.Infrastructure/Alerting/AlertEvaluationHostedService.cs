namespace RPA.Infrastructure.Alerting;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// Alarm motoru arka plan servisi (WP-6.3, Spec Bölüm 8.2, 11). Periyodik olarak metrik anlık
/// görüntüsü alır, aktif kuralları değerlendirir ve tetiklenenler için bildirim gönderir.
/// Değerlendirme aralığı ve pencere yapılandırılabilir (Alerting:IntervalSeconds / WindowMinutes).
/// </summary>
public sealed class AlertEvaluationHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AlertEvaluationHostedService> _logger;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _window;

    public AlertEvaluationHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<AlertEvaluationHostedService> logger,
        AlertEngineOptions options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _interval = TimeSpan.FromSeconds(options.IntervalSeconds <= 0 ? 60 : options.IntervalSeconds);
        _window = TimeSpan.FromMinutes(options.WindowMinutes <= 0 ? 60 : options.WindowMinutes);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var provider = scope.ServiceProvider.GetRequiredService<IAlertMetricsProvider>();
                var evaluator = scope.ServiceProvider.GetRequiredService<AlertEvaluationService>();

                var metrics = await provider.GetAsync(_window, stoppingToken).ConfigureAwait(false);
                var fired = await evaluator.EvaluateAndDispatchAsync(metrics, stoppingToken).ConfigureAwait(false);
                if (fired > 0)
                {
                    _logger.LogInformation("Alarm motoru: {Count} kural tetiklendi ve bildirim gönderildi.", fired);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Alarm motoru değerlendirme döngüsünde hata.");
            }

            try
            {
                await Task.Delay(_interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}

/// <summary>Alarm motoru yapılandırması (Alerting bölümü).</summary>
public sealed class AlertEngineOptions
{
    public int IntervalSeconds { get; set; } = 60;
    public int WindowMinutes { get; set; } = 60;
}

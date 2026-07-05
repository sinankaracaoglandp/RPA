namespace RPA.Infrastructure.Scheduling;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RPA.Domain.Interfaces;

/// <summary>
/// Zamanlayıcı arka plan servisi (Task 3.3, Spec Bölüm 7). Periyodik olarak aktif Cron
/// tetikleyicilerini tarar; ilişkili Schedule'ın cron ifadesine göre ateşleme zamanı gelmişse
/// ITriggerService.ExecuteTriggerAsync çağrılır. Ateşleme, tetikleyici bazında son ateşleme
/// zamanı (bellek içi) ile tekrar tetiklenmeyi engeller.
/// </summary>
public sealed class SchedulerHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SchedulerHostedService> _logger;
    private readonly SchedulerOptions _options;
    private readonly Dictionary<Guid, DateTime> _lastFireUtc = new();

    public SchedulerHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<SchedulerHostedService> logger,
        IOptions<SchedulerOptions>? options = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? new SchedulerOptions();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Zamanlayıcı tarama sırasında beklenmeyen hata.");
            }

            try
            {
                await Task.Delay(_options.PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Servis durduruluyor.
            }
        }
    }

    /// <summary>
    /// Tek bir tarama turu: aktif Cron tetikleyicilerini bulur, her biri için son ateşlemeden
    /// bu yana cron'un tetiklenmesi gerekip gerekmediğini kontrol eder, gerekiyorsa
    /// ITriggerService.ExecuteTriggerAsync çağırır. Test edilebilirlik için public.
    /// </summary>
    public async Task ScanOnceAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITriggerRepository>();
        var triggerService = scope.ServiceProvider.GetRequiredService<ITriggerService>();

        var triggers = await repository.FindActiveCronTriggersAsync(cancellationToken);
        var now = DateTime.UtcNow;

        foreach (var trigger in triggers)
        {
            var schedule = await repository.FindScheduleByTriggerIdAsync(trigger.Id, cancellationToken);
            if (schedule is null)
                continue;

            var lastFire = _lastFireUtc.TryGetValue(trigger.Id, out var lf) ? lf : trigger.CreatedAt;
            if (!CronScheduleCalculator.IsDue(schedule.CronExpression, schedule.TimeZone, lastFire, now))
                continue;

            _lastFireUtc[trigger.Id] = now;
            _logger.LogInformation("Zamanlayıcı: Trigger {TriggerId} cron zamanı geldi — ateşleniyor.", trigger.Id);
            await triggerService.ExecuteTriggerAsync(trigger.Id, "cron", cancellationToken);
        }
    }
}

/// <summary>Zamanlayıcı ayarları (tarama aralığı).</summary>
public sealed class SchedulerOptions
{
    public const string SectionName = "Scheduler";

    /// <summary>Tarama aralığı. Varsayılan 30 saniye.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);
}

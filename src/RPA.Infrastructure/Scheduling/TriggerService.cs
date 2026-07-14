namespace RPA.Infrastructure.Scheduling;

using Microsoft.Extensions.Logging;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Exceptions;
using RPA.Domain.Interfaces;

/// <summary>
/// Tetikleyici yürütme servisi (Task 3.3, Spec Bölüm 7). Bir tetikleyici ateşlendiğinde JobRun
/// oluşturur; Cron tetikleyicilerde Schedule.OverlapPolicy'ye göre hemen çalıştırır (parallel),
/// atlar (skip) ya da kuyruğa alır (queue). Correlation ID = JobRun.Id.
/// </summary>
public sealed class TriggerService : ITriggerService
{
    private const string PolicySkip = "skip";
    private const string PolicyQueue = "queue";
    private const string PolicyParallel = "parallel";

    private readonly ITriggerRepository _repository;
    private readonly IWorkflowRunner _workflowRunner;
    private readonly IRobotDispatcher _dispatcher;
    private readonly ILogger<TriggerService> _logger;

    public TriggerService(ITriggerRepository repository, IWorkflowRunner workflowRunner,
        IRobotDispatcher dispatcher, ILogger<TriggerService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _workflowRunner = workflowRunner ?? throw new ArgumentNullException(nameof(workflowRunner));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<TriggerExecutionResult> ExecuteTriggerAsync(
        Guid triggerId, string triggeredBy, CancellationToken cancellationToken = default)
    {
        var trigger = await _repository.FindTriggerByIdAsync(triggerId, cancellationToken);
        if (trigger is null)
        {
            _logger.LogWarning("ExecuteTrigger: bilinmeyen Trigger {TriggerId}.", triggerId);
            return TriggerExecutionResult.NotFound();
        }

        if (!trigger.IsActive)
        {
            _logger.LogInformation("Trigger {TriggerId} pasif — ateşlenmedi.", triggerId);
            return TriggerExecutionResult.Inactive();
        }

        var policy = PolicyParallel;
        if (trigger.Type == TriggerType.Cron)
        {
            var schedule = await _repository.FindScheduleByTriggerIdAsync(triggerId, cancellationToken);
            policy = schedule?.OverlapPolicy?.Trim().ToLowerInvariant() ?? PolicyParallel;
        }

        var hasRunning = await _repository.HasRunningJobRunAsync(trigger.WorkflowVersionId, cancellationToken);

        if (hasRunning && policy == PolicySkip)
        {
            _logger.LogInformation(
                "Trigger {TriggerId}: çalışan JobRun mevcut, OverlapPolicy=skip — ateşleme atlandı.", triggerId);
            return TriggerExecutionResult.Skipped(null);
        }

        if (hasRunning && policy == PolicyQueue)
        {
            var queued = NewJobRun(trigger, triggeredBy, status: "Queued");
            await _repository.AddJobRunAsync(queued, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Trigger {TriggerId}: çalışan JobRun mevcut, OverlapPolicy=queue — JobRun {JobRunId} kuyruğa alındı.",
                triggerId, queued.Id);
            return TriggerExecutionResult.Queued(queued);
        }

        // parallel ya da hiç çalışan yok: uygun ajan seç ve başlat.
        var robot = await _dispatcher.SelectRobotAsync(trigger, cancellationToken);
        var status = robot is null ? "Pending" : "Running";
        var jobRun = NewJobRun(trigger, triggeredBy, status: status);
        jobRun.AssignedRobotId = robot?.Id;
        await _repository.AddJobRunAsync(jobRun, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        if (robot is null)
        {
            _logger.LogInformation(
                "Trigger {TriggerId}: uygun ajan yok — JobRun {JobRunId} Pending.", triggerId, jobRun.Id);
            return TriggerExecutionResult.Pending(jobRun);
        }

        _logger.LogInformation(
            "Trigger {TriggerId} ateşlendi — JobRun {JobRunId} başlatıldı (correlationId={CorrelationId}).",
            triggerId, jobRun.Id, jobRun.ElasticsearchCorrelationId);

        await RunAndFinalizeAsync(jobRun, cancellationToken);
        return TriggerExecutionResult.Executed(jobRun);
    }

    private JobRun NewJobRun(Trigger trigger, string triggeredBy, string status) => new()
    {
        WorkflowVersionId = trigger.WorkflowVersionId,
        TriggeredBy = triggeredBy,
        EnvironmentId = trigger.EnvironmentId,
        Status = status,
        StartedAt = DateTime.UtcNow,
        ElasticsearchCorrelationId = Guid.NewGuid().ToString(),
    };

    private async Task RunAndFinalizeAsync(JobRun jobRun, CancellationToken cancellationToken)
    {
        // Not: Gerçek WorkflowVersion + argümanlar Task 3.4/İş Akışı Motoru entegrasyonunda tam
        // olarak bağlanacaktır. Burada IWorkflowRunner sözleşmesi çağrılır; JobRun.WorkflowVersion
        // navigasyonu henüz tam şemada değil (bkz. RpaDbContext Ignore) — placeholder ile çağrılır.
        try
        {
            var result = await _workflowRunner.ExecuteAsync(
                jobRun.WorkflowVersion, new Dictionary<string, object?>(), jobRun.Id, cancellationToken);

            jobRun.Status = result.Success ? "Successful" : (result.Exception is BusinessException ? "BusinessException" : "Failed");
        }
        catch (BusinessException)
        {
            jobRun.Status = "BusinessException";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JobRun {JobRunId} çalıştırılırken hata.", jobRun.Id);
            jobRun.Status = "Failed";
        }
        finally
        {
            jobRun.CompletedAt = DateTime.UtcNow;
            await _repository.SaveChangesAsync(cancellationToken);
        }

        await ProcessNextQueuedAsync(jobRun.WorkflowVersionId, cancellationToken);
    }

    /// <summary>Bir JobRun tamamlandığında, aynı workflow versiyonu için OverlapPolicy=queue ile
    /// biriktirilmiş en eski JobRun varsa onu başlatır (Spec Bölüm 7).</summary>
    private async Task ProcessNextQueuedAsync(Guid workflowVersionId, CancellationToken cancellationToken)
    {
        var next = await _repository.FindOldestQueuedJobRunAsync(workflowVersionId, cancellationToken);
        if (next is null)
            return;

        next.Status = "Running";
        next.StartedAt = DateTime.UtcNow;
        await _repository.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Kuyruktaki JobRun {JobRunId} başlatıldı (OverlapPolicy=queue).", next.Id);

        await RunAndFinalizeAsync(next, cancellationToken);
    }
}

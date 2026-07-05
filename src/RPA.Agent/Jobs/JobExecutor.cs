namespace RPA.Agent.Jobs;

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RPA.Agent.State;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Retry;

/// <summary>
/// Tek bir işi izole biçimde çalıştırır: BaseRunner'ı (<see cref="IWorkflowRunner"/>) çağırır,
/// çıktıyı yakalar, istisnayı <see cref="ExceptionClassifier"/> ile Business/System olarak
/// sınıflandırır. Correlation ID = QueueItem kimliği (Spec Bölüm 11). Durum sayaçlarını günceller.
/// </summary>
public sealed class JobExecutor
{
    private readonly IWorkflowRunner _runner;
    private readonly ExceptionClassifier _classifier;
    private readonly IAgentState _state;
    private readonly ILogger<JobExecutor> _logger;

    public JobExecutor(
        IWorkflowRunner runner,
        ExceptionClassifier classifier,
        IAgentState state,
        ILogger<JobExecutor> logger)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// İşi çalıştırır ve sonucu döndürür. İstisnalar yakalanır ve sınıflandırılır; çağıran
    /// (yoklama servisi) sonuca göre kuyruğa başarı/başarısızlık raporlar.
    /// </summary>
    public async Task<JobExecutionOutcome> ExecuteAsync(AgentJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        var sw = Stopwatch.StartNew();
        _state.SetCurrentJob(job.ItemId);
        _state.SetActivity(AgentActivity.Running);
        _logger.LogInformation("İş {ItemId} başlatıldı (WorkflowVersion={WorkflowVersionId}).",
            job.ItemId, job.WorkflowVersion.Id);

        try
        {
            var result = await _runner.ExecuteAsync(
                job.WorkflowVersion, job.Arguments, job.ItemId, cancellationToken);
            sw.Stop();

            if (result.Success)
            {
                _state.RecordJobCompleted();
                _logger.LogInformation("İş {ItemId} başarıyla tamamlandı ({DurationMs} ms).",
                    job.ItemId, sw.ElapsedMilliseconds);
                return JobExecutionOutcome.Succeeded(result.Outputs, sw.ElapsedMilliseconds);
            }

            // Runner başarısız döndü ama fırlatmadı — sonuçtaki istisnayı sınıflandır.
            var ex = result.Exception ?? new RPA.Domain.Exceptions.SystemException(
                $"İş {job.ItemId} başarısız (istisna bilgisi yok).");
            return Classify(job, ex, sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            _logger.LogWarning("İş {ItemId} iptal edildi (servis durduruluyor).", job.ItemId);
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            return Classify(job, ex, sw.ElapsedMilliseconds);
        }
        finally
        {
            _state.SetCurrentJob(null);
            _state.SetActivity(_state.IsPaused ? AgentActivity.Paused : AgentActivity.Idle);
        }
    }

    private JobExecutionOutcome Classify(AgentJob job, Exception ex, long durationMs)
    {
        var type = _classifier.Classify(ex);
        _state.RecordJobFailed();
        if (type == ExceptionType.Business)
        {
            _logger.LogWarning(ex, "İş {ItemId} iş kuralı istisnasıyla başarısız (retry yok).", job.ItemId);
        }
        else
        {
            _logger.LogError(ex, "İş {ItemId} sistem istisnasıyla başarısız (retry edilebilir).", job.ItemId);
        }
        return JobExecutionOutcome.Failed(ex, type, durationMs);
    }
}

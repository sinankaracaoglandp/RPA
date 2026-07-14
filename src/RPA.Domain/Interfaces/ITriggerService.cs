namespace RPA.Domain.Interfaces;

using RPA.Domain.Entities;

/// <summary>
/// Tetikleyici yürütme servisi (Task 3.3, Spec Bölüm 7 — Triggers). Bir tetikleyici ateşlendiğinde
/// (cron zamanı geldi / manuel / API) JobRun oluşturur ve OverlapPolicy'ye göre hemen çalıştırır,
/// kuyruğa alır ya da atlar.
/// </summary>
public interface ITriggerService
{
    /// <summary>
    /// Tetikleyiciyi ateşler. Trigger bulunamaz veya IsActive=false ise sonuç Outcome=NotFound/Inactive olur.
    /// Cron tetikleyicilerde ilişkili Schedule.OverlapPolicy uygulanır:
    /// skip (çalışan varsa atla), queue (çalışan varsa JobRun'ı Queued olarak biriktir),
    /// parallel (her zaman yeni JobRun başlatıp çalıştırır).
    /// </summary>
    Task<TriggerExecutionResult> ExecuteTriggerAsync(
        Guid triggerId, string triggeredBy, CancellationToken cancellationToken = default);
}

/// <summary>Tetikleyici ateşleme sonucu.</summary>
public sealed class TriggerExecutionResult
{
    public TriggerExecutionOutcome Outcome { get; init; }

    public JobRun? JobRun { get; init; }

    public static TriggerExecutionResult NotFound() => new() { Outcome = TriggerExecutionOutcome.NotFound };

    public static TriggerExecutionResult Inactive() => new() { Outcome = TriggerExecutionOutcome.Inactive };

    public static TriggerExecutionResult Skipped(JobRun? previous) => new() { Outcome = TriggerExecutionOutcome.Skipped, JobRun = previous };

    public static TriggerExecutionResult Queued(JobRun jobRun) => new() { Outcome = TriggerExecutionOutcome.Queued, JobRun = jobRun };

    public static TriggerExecutionResult Executed(JobRun jobRun) => new() { Outcome = TriggerExecutionOutcome.Executed, JobRun = jobRun };

    public static TriggerExecutionResult Pending(JobRun jobRun) => new() { Outcome = TriggerExecutionOutcome.Pending, JobRun = jobRun };
}

public enum TriggerExecutionOutcome
{
    NotFound,
    Inactive,
    /// <summary>OverlapPolicy=skip: hâlihazırda çalışan bir JobRun olduğu için ateşlenmedi.</summary>
    Skipped,
    /// <summary>OverlapPolicy=queue: hâlihazırda çalışan bir JobRun olduğu için yeni JobRun Queued olarak eklendi.</summary>
    Queued,
    /// <summary>JobRun oluşturuldu ve hemen çalıştırıldı (parallel veya çalışan yoktu).</summary>
    Executed,
    /// <summary>Uygun ajan bulunamadı — JobRun Pending olarak park edildi, çalıştırılmadı.</summary>
    Pending,
}

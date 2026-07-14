namespace RPA.Domain.Interfaces;

using RPA.Domain.Entities;

/// <summary>
/// Trigger/Schedule/JobRun kalıcılık soyutlaması (Task 3.3, Spec Bölüm 7 — Triggers).
/// </summary>
public interface ITriggerRepository
{
    Task<Trigger?> FindTriggerByIdAsync(Guid triggerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Trigger>> FindTriggersByWorkflowVersionAsync(Guid workflowVersionId, CancellationToken cancellationToken = default);

    /// <summary>Aktif (IsActive) ve tipi Cron olan tüm tetikleyicileri döner (zamanlayıcı taraması için).</summary>
    Task<IReadOnlyList<Trigger>> FindActiveCronTriggersAsync(CancellationToken cancellationToken = default);

    Task<Schedule?> FindScheduleByTriggerIdAsync(Guid triggerId, CancellationToken cancellationToken = default);

    Task AddTriggerAsync(Trigger trigger, CancellationToken cancellationToken = default);

    Task AddScheduleAsync(Schedule schedule, CancellationToken cancellationToken = default);

    /// <summary>Verilen workflow versiyonu için Running durumundaki bir JobRun olup olmadığını döner
    /// (OverlapPolicy = skip/queue kontrolü — Spec Bölüm 7).</summary>
    Task<bool> HasRunningJobRunAsync(Guid workflowVersionId, CancellationToken cancellationToken = default);

    /// <summary>Verilen workflow versiyonu için en eski Queued JobRun'ı döner; yoksa null
    /// (OverlapPolicy = queue — önceki çalışma bitince sıradaki başlatılır).</summary>
    Task<JobRun?> FindOldestQueuedJobRunAsync(Guid workflowVersionId, CancellationToken cancellationToken = default);

    Task AddJobRunAsync(JobRun jobRun, CancellationToken cancellationToken = default);

    /// <summary>Tüm trigger'ları (job tanımları) opsiyonel filtrelerle döner (Studio Zamanlamalar ekranı).</summary>
    Task<IReadOnlyList<Trigger>> ListTriggersAsync(
        Guid? projectId, Guid? environmentId, bool? isActive, CancellationToken cancellationToken = default);

    /// <summary>Status=="Running" JobRun'ları AssignedRobotId'ye göre sayar (kapasite kontrolü — dispatcher).</summary>
    Task<IReadOnlyDictionary<Guid, int>> GetActiveJobCountsByRobotAsync(CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

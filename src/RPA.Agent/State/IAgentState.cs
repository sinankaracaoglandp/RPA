namespace RPA.Agent.State;

/// <summary>
/// Ajanın çalışma zamanı durumu (tray gösterimi + servis koordinasyonu için paylaşılan).
/// Thread-safe: heartbeat, poll ve UI iş parçacıkları eşzamanlı erişir.
/// </summary>
public interface IAgentState
{
    /// <summary>Orchestrator'a kayıt sonrası atanan robot kimliği (correlation ID kaynağı).</summary>
    Guid? RobotId { get; }

    /// <summary>Ajanın anlık faaliyet durumu.</summary>
    AgentActivity Activity { get; }

    /// <summary>Şu anda çalışan işin (QueueItem) kimliği; boştaysa null.</summary>
    Guid? CurrentJobId { get; }

    /// <summary>Başarıyla tamamlanan iş sayısı.</summary>
    int CompletedJobCount { get; }

    /// <summary>Başarısız iş sayısı.</summary>
    int FailedJobCount { get; }

    /// <summary>Son heartbeat zamanı (UTC).</summary>
    DateTime? LastHeartbeatUtc { get; }

    /// <summary>Kullanıcının duraklattığı mod (tray'den). Duraklatıldıysa yoklama iş almaz.</summary>
    bool IsPaused { get; }

    void SetRobotId(Guid robotId);
    void SetActivity(AgentActivity activity);
    void SetCurrentJob(Guid? jobId);
    void RecordJobCompleted();
    void RecordJobFailed();
    void RecordHeartbeat(DateTime utcNow);
    void SetPaused(bool paused);
}

/// <summary>Ajan faaliyet durumu (tray ikonunda gösterilir).</summary>
public enum AgentActivity
{
    Starting,
    Registering,
    Idle,
    Running,
    Paused,
    Stopped,
}

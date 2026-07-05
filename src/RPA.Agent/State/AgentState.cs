namespace RPA.Agent.State;

/// <summary>
/// <see cref="IAgentState"/> thread-safe implementasyonu. Alanlar bir kilit altında güncellenir;
/// sayaçlar <see cref="System.Threading.Interlocked"/> ile artırılır.
/// </summary>
public sealed class AgentState : IAgentState
{
    private readonly object _gate = new();
    private Guid? _robotId;
    private AgentActivity _activity = AgentActivity.Starting;
    private Guid? _currentJobId;
    private int _completed;
    private int _failed;
    private DateTime? _lastHeartbeatUtc;
    private bool _isPaused;

    public Guid? RobotId { get { lock (_gate) return _robotId; } }
    public AgentActivity Activity { get { lock (_gate) return _activity; } }
    public Guid? CurrentJobId { get { lock (_gate) return _currentJobId; } }
    public int CompletedJobCount => Volatile.Read(ref _completed);
    public int FailedJobCount => Volatile.Read(ref _failed);
    public DateTime? LastHeartbeatUtc { get { lock (_gate) return _lastHeartbeatUtc; } }
    public bool IsPaused { get { lock (_gate) return _isPaused; } }

    public void SetRobotId(Guid robotId) { lock (_gate) _robotId = robotId; }
    public void SetActivity(AgentActivity activity) { lock (_gate) _activity = activity; }
    public void SetCurrentJob(Guid? jobId) { lock (_gate) _currentJobId = jobId; }
    public void RecordJobCompleted() => Interlocked.Increment(ref _completed);
    public void RecordJobFailed() => Interlocked.Increment(ref _failed);
    public void RecordHeartbeat(DateTime utcNow) { lock (_gate) _lastHeartbeatUtc = utcNow; }

    public void SetPaused(bool paused)
    {
        lock (_gate)
        {
            _isPaused = paused;
            _activity = paused ? AgentActivity.Paused : AgentActivity.Idle;
        }
    }
}

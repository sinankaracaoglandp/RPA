namespace RPA.Agent.Hub;

/// <summary>
/// RobotHub'ın "JobStatusChanged" mesajının yükü (Spec Bölüm 9). Orchestrator/JobExecutor tarafında
/// üretilir, agent tarafında SignalR istemcisi ile alınır ve <see cref="JobEventRouter"/> aracılığıyla
/// Job List penceresine yansıtılır.
/// </summary>
public sealed class JobEventDto
{
    public Guid JobId { get; set; }
    public string WorkflowName { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty; // "Started" | "StepChanged" | "Completed" | "Failed"
    public string? CurrentStep { get; set; }
}

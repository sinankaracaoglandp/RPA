namespace RPA.Agent.JobList;

/// <summary>Job List penceresinde bir satır: iş, workflow adı, başlangıç zamanı, geçerli adım, durum.</summary>
public sealed class JobListItem
{
    public JobListItem(Guid jobId, string workflowName, DateTime startedUtc)
    {
        JobId = jobId;
        WorkflowName = workflowName ?? throw new ArgumentNullException(nameof(workflowName));
        StartedUtc = startedUtc;
        CurrentStep = string.Empty;
        Status = JobListStatus.Running;
    }

    public Guid JobId { get; }
    public string WorkflowName { get; }
    public DateTime StartedUtc { get; }
    public string CurrentStep { get; set; }
    public JobListStatus Status { get; set; }
}

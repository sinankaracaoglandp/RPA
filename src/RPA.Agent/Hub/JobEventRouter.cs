namespace RPA.Agent.Hub;

using Microsoft.Extensions.Logging;
using RPA.Agent.JobList;

/// <summary>
/// SignalR üzerinden alınan <see cref="JobEventDto"/> mesajlarını <see cref="JobListViewModel"/>
/// güncellemelerine çevirir (Spec Bölüm 9 — Job List gerçek zamanlı güncelleme). SignalR istemcisinden
/// bağımsızdır — böylece mesaj yönlendirme mantığı gerçek ağ olmadan test edilebilir.
/// </summary>
public sealed class JobEventRouter
{
    private readonly JobListViewModel _jobList;
    private readonly ILogger<JobEventRouter> _logger;

    public JobEventRouter(JobListViewModel jobList, ILogger<JobEventRouter> logger)
    {
        _jobList = jobList ?? throw new ArgumentNullException(nameof(jobList));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Handle(JobEventDto jobEvent)
    {
        ArgumentNullException.ThrowIfNull(jobEvent);

        switch (jobEvent.EventType)
        {
            case "Started":
                _jobList.AddOrUpdate(new JobListItem(jobEvent.JobId, jobEvent.WorkflowName, DateTime.UtcNow));
                break;
            case "StepChanged":
                _jobList.UpdateStep(jobEvent.JobId, jobEvent.CurrentStep ?? string.Empty);
                break;
            case "Completed":
                _jobList.Complete(jobEvent.JobId, success: true);
                break;
            case "Failed":
                _jobList.Complete(jobEvent.JobId, success: false);
                break;
            default:
                _logger.LogWarning("Bilinmeyen JobEvent türü: {EventType} (JobId={JobId})", jobEvent.EventType, jobEvent.JobId);
                break;
        }
    }
}

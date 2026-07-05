namespace RPA.Agent.JobList;

/// <summary>
/// Job List penceresinin gerçek zamanlı veri modeli (Spec Bölüm 9). SignalR'dan gelen olaylar
/// (JobStatusChanged) bu modele yansıtılır; UI <see cref="Changed"/> olayına abone olup
/// dispatcher üzerinden yeniden çizer. Thread-safe: birden fazla iş parçacığından güncellenebilir.
/// </summary>
public sealed class JobListViewModel
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, JobListItem> _items = new();

    /// <summary>Liste her değiştiğinde tetiklenir (UI thread'ine marshal edilmesi UI'nin sorumluluğundadır).</summary>
    public event Action? Changed;

    public void AddOrUpdate(JobListItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        lock (_gate)
            _items[item.JobId] = item;
        Changed?.Invoke();
    }

    public void UpdateStep(Guid jobId, string currentStep)
    {
        lock (_gate)
        {
            if (_items.TryGetValue(jobId, out var item))
                item.CurrentStep = currentStep;
        }
        Changed?.Invoke();
    }

    public void Complete(Guid jobId, bool success)
    {
        lock (_gate)
        {
            if (_items.TryGetValue(jobId, out var item))
                item.Status = success ? JobListStatus.Completed : JobListStatus.Failed;
        }
        Changed?.Invoke();
    }

    public void Remove(Guid jobId)
    {
        bool removed;
        lock (_gate)
            removed = _items.Remove(jobId);
        if (removed)
            Changed?.Invoke();
    }

    /// <summary>Anlık listenin salt-okunur kopyası (UI thread'inde güvenle numaralandırılabilir).</summary>
    public IReadOnlyList<JobListItem> GetSnapshot()
    {
        lock (_gate)
            return _items.Values.ToList();
    }
}

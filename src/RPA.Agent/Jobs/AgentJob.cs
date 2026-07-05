namespace RPA.Agent.Jobs;

using RPA.Domain.Entities;

/// <summary>
/// Ajanın çalıştıracağı çözümlenmiş iş: kuyruk kaleminin kimliği, hedef workflow sürümü ve
/// giriş argümanları. Kuyruk kaleminin payload'undan <see cref="IAgentJobSource"/> tarafından üretilir.
/// </summary>
public sealed class AgentJob
{
    public AgentJob(Guid itemId, WorkflowVersion workflowVersion, Dictionary<string, object?> arguments)
    {
        ItemId = itemId;
        WorkflowVersion = workflowVersion ?? throw new ArgumentNullException(nameof(workflowVersion));
        Arguments = arguments ?? new Dictionary<string, object?>();
    }

    /// <summary>Kuyruk kaleminin (QueueItem) kimliği — correlation ID.</summary>
    public Guid ItemId { get; }

    /// <summary>Çalıştırılacak workflow sürümü.</summary>
    public WorkflowVersion WorkflowVersion { get; }

    /// <summary>Workflow giriş argümanları.</summary>
    public Dictionary<string, object?> Arguments { get; }
}

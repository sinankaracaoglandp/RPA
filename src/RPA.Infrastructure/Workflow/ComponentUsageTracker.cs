namespace RPA.Infrastructure.Workflow;

using RPA.Domain.Entities;
using RPA.Domain.Interfaces;

/// <summary>
/// <see cref="IComponentUsageTracker"/> — bir çalıştırma boyunca çağrılan component
/// versiyonlarını bellek-içi biriktirir. (workflowVersionId, componentVersionId) çifti
/// tekilleştirilir. Thread-safe (paralel node yürütme olasılığına karşı).
/// </summary>
public sealed class ComponentUsageTracker : IComponentUsageTracker
{
    private readonly object _gate = new();
    private readonly HashSet<(Guid Workflow, Guid Component)> _seen = new();
    private readonly List<ComponentUsage> _usages = new();

    /// <inheritdoc />
    public void Record(Guid workflowVersionId, ComponentVersion componentVersion)
    {
        ArgumentNullException.ThrowIfNull(componentVersion);

        lock (_gate)
        {
            var key = (workflowVersionId, componentVersion.Id);
            if (!_seen.Add(key))
            {
                return;
            }

            _usages.Add(new ComponentUsage
            {
                WorkflowVersionId = workflowVersionId,
                ComponentVersionId = componentVersion.Id,
                ComponentVersion = componentVersion,
            });
        }
    }

    /// <inheritdoc />
    public IReadOnlyCollection<ComponentUsage> Usages
    {
        get
        {
            lock (_gate)
            {
                return _usages.ToArray();
            }
        }
    }
}

namespace RPA.Agent.Jobs;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RPA.Agent.Configuration;
using RPA.Agent.State;
using RPA.Domain.Interfaces;

/// <summary>
/// Kuyruk motorunu (<see cref="IQueueService"/>) saran varsayılan iş kaynağı. Yapılandırılan
/// kuyruktan sıradaki kalemi atomik olarak (UPDLOCK) çeker, payload'ını çözer ve raporlamayı
/// kuyruğun Complete/Fail geçişlerine yönlendirir. Robot kimliği ajan durumundan alınır.
/// </summary>
public sealed class QueueAgentJobSource : IAgentJobSource
{
    private readonly IQueueService _queue;
    private readonly IAgentState _state;
    private readonly AgentOptions _options;
    private readonly ILogger<QueueAgentJobSource> _logger;

    public QueueAgentJobSource(
        IQueueService queue,
        IAgentState state,
        IOptions<AgentOptions> options,
        ILogger<QueueAgentJobSource> logger)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AgentJob?> DequeueAsync(CancellationToken cancellationToken = default)
    {
        var robotId = _state.RobotId
            ?? throw new InvalidOperationException("Robot henüz kaydedilmedi; iş çekilemez.");

        var queueId = await ResolveQueueIdAsync(cancellationToken).ConfigureAwait(false);
        if (queueId is null)
        {
            return null;
        }

        var item = await _queue.GetNextItemAsync(queueId.Value, robotId, cancellationToken);
        if (item is null)
            return null;

        try
        {
            return AgentJobPayloadParser.Parse(item.Id, item.Payload);
        }
        catch (FormatException ex)
        {
            // Bozuk payload iş kuralı hatasıdır — retry etmeden başarısız işaretle (Action Center).
            _logger.LogWarning(ex, "İş {ItemId}: payload çözümlenemedi, kalem başarısız işaretlendi.", item.Id);
            await _queue.FailAsync(item.Id, ex.Message, isBusinessException: true, cancellationToken);
            return null;
        }
    }

    public async Task ReportSuccessAsync(Guid itemId, CancellationToken cancellationToken = default)
        => await _queue.CompleteAsync(itemId, cancellationToken);

    public async Task ReportFailureAsync(Guid itemId, string? errorDetail, bool isBusinessException, CancellationToken cancellationToken = default)
        => await _queue.FailAsync(itemId, errorDetail, isBusinessException, cancellationToken);

    private async Task<Guid?> ResolveQueueIdAsync(CancellationToken cancellationToken)
    {
        if (_options.QueueId != Guid.Empty)
        {
            return _options.QueueId;
        }

        if (string.IsNullOrWhiteSpace(_options.QueueName))
        {
            throw new InvalidOperationException("Agent kuyruğu yapılandırılmadı; QueueId veya QueueName verilmelidir.");
        }

        var queues = await _queue.ListQueuesAsync(cancellationToken).ConfigureAwait(false);
        var queue = queues.FirstOrDefault(q => string.Equals(q.Name, _options.QueueName, StringComparison.OrdinalIgnoreCase));
        if (queue is null)
        {
            _logger.LogDebug("Agent kuyruğu bulunamadı: {QueueName}", _options.QueueName);
            return null;
        }

        return queue.Id;
    }
}

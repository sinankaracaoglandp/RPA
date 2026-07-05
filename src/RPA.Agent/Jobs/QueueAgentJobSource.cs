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

        var item = await _queue.GetNextItemAsync(_options.QueueId, robotId, cancellationToken);
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
}

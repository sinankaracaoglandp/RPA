namespace RPA.Agent.Jobs;

using Microsoft.Extensions.Logging;
using RPA.Agent.Hub;
using RPA.Domain.Interfaces;

/// <summary>
/// <see cref="IWorkflowExecutionObserver"/>'ın ajan implementasyonu — BaseRunner'ın ürettiği
/// node yaşam döngüsü olaylarını <see cref="IJobHubClient"/> üzerinden orchestrator'a (RobotHub)
/// iletir; oradan Studio canlı konsoluna yayılır.
///
/// <para>Ateşle-unut (fire-and-forget): gözlemci çağrıları yürütmeyi bloke etmez ve hata
/// fırlatmaz (bağlantı yoksa/gönderim başarısızsa sessizce yutulur) — canlı konsol best-effort'tur.</para>
/// </summary>
public sealed class AgentWorkflowExecutionObserver : IWorkflowExecutionObserver
{
    private readonly IJobHubClient _hub;
    private readonly ILogger<AgentWorkflowExecutionObserver> _logger;

    public AgentWorkflowExecutionObserver(IJobHubClient hub, ILogger<AgentWorkflowExecutionObserver> logger)
    {
        _hub = hub ?? throw new ArgumentNullException(nameof(hub));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void OnNodeStarted(NodeExecutionEvent evt) => Forward(evt);

    public void OnNodeCompleted(NodeExecutionEvent evt) => Forward(evt);

    private void Forward(NodeExecutionEvent evt)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _hub.ReportNodeLogAsync(evt);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Canlı node logu iletilemedi (yok sayıldı) {NodeId}.", evt.NodeId);
            }
        });
    }
}

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

    public void OnNodeCompleted(NodeExecutionEvent evt)
    {
        // Ajanın KENDİ konsoluna da yaz: Studio konsolu SignalR köprüsüne bağlıdır (bağlantı
        // yoksa/orchestrator erişilemezse hiçbir şey görünmez). Değişken anlık görüntüsü zaten
        // maskelenmiştir (BaseRunner), dolayısıyla loglanması güvenlidir.
        _logger.LogInformation(
            "Node {NodeId} ({Activity}) tamamlandı — çıkışlar: {Outputs} | değişkenler: {Variables}",
            evt.NodeId,
            evt.ActivityId ?? evt.NodeType,
            Describe(evt.Outputs),
            Describe(evt.Variables));

        Forward(evt);
    }

    private static string Describe(IReadOnlyDictionary<string, string?>? values)
        => values is null || values.Count == 0
            ? "-"
            : string.Join(", ", values.Select(kv => $"{kv.Key}={kv.Value ?? "∅"}"));

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
                // Uyarı seviyesi: Studio konsolunun boş kalmasının sebebi görünür olmalı
                // (Debug seviyesi ajanın varsayılan Information ayarında hiç basılmıyordu).
                _logger.LogWarning(ex, "Canlı node logu iletilemedi (yok sayıldı) {NodeId}.", evt.NodeId);
            }
        });
    }
}

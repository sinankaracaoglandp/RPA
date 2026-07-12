namespace RPA.Agent.Hub;

using RPA.Domain.Interfaces;

/// <summary>Ajanın RobotHub'a (SignalR) bağlanıp iş olaylarını dinlediği istemci sözleşmesi.</summary>
public interface IJobHubClient : IAsyncDisposable
{
    Task StartAsync(Guid robotId, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Yürütme sırasında bir node yaşam döngüsü olayını orchestrator'a (RobotHub.ReportNodeLog)
    /// iletir; oradan Studio canlı konsoluna yayılır. Bağlantı yoksa sessizce yok sayılır.
    /// </summary>
    Task ReportNodeLogAsync(NodeExecutionEvent evt, CancellationToken cancellationToken = default);
}

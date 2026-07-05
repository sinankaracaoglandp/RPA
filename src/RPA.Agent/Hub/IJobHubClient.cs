namespace RPA.Agent.Hub;

/// <summary>Ajanın RobotHub'a (SignalR) bağlanıp iş olaylarını dinlediği istemci sözleşmesi.</summary>
public interface IJobHubClient : IAsyncDisposable
{
    Task StartAsync(Guid robotId, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}

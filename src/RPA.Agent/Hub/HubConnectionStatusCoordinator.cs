namespace RPA.Agent.Hub;

using Microsoft.Extensions.Logging;

/// <summary>
/// SignalR (RobotHub) istemci bağlantısının yaşam döngüsü olaylarını (Closed/Reconnecting/Reconnected)
/// tray'in gösterdiği <see cref="ConnectionStatus"/>'a çevirir. Microsoft.AspNetCore.SignalR.Client'ın
/// HubConnection'ından bağımsızdır — böylece gerçek ağ olmadan birim test edilebilir. Gerçek
/// HubConnection'ın Closed/Reconnecting/Reconnected olayları bu sınıfın karşılık gelen metotlarını çağırır.
/// </summary>
public sealed class HubConnectionStatusCoordinator
{
    private readonly ILogger<HubConnectionStatusCoordinator> _logger;
    private ConnectionStatus _status = ConnectionStatus.Offline;

    public HubConnectionStatusCoordinator(ILogger<HubConnectionStatusCoordinator> logger)
        => _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public ConnectionStatus Status
    {
        get => _status;
        private set
        {
            if (_status == value)
                return;
            _status = value;
            StatusChanged?.Invoke(value);
        }
    }

    /// <summary>Durum her değiştiğinde tetiklenir (tray tooltip/ikonunu güncellemek için).</summary>
    public event Action<ConnectionStatus>? StatusChanged;

    public void OnConnected()
    {
        _logger.LogInformation("RobotHub bağlantısı kuruldu.");
        Status = ConnectionStatus.Online;
    }

    public void OnReconnecting(Exception? error)
    {
        _logger.LogWarning(error, "RobotHub bağlantısı koptu, yeniden bağlanılıyor…");
        Status = ConnectionStatus.Reconnecting;
    }

    public void OnReconnected()
    {
        _logger.LogInformation("RobotHub bağlantısı yeniden kuruldu.");
        Status = ConnectionStatus.Online;
    }

    public void OnClosed(Exception? error)
    {
        _logger.LogError(error, "RobotHub bağlantısı kapandı — çevrimdışı.");
        Status = ConnectionStatus.Offline;
    }
}

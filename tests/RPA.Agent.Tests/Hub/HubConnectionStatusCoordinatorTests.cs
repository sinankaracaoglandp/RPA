namespace RPA.Agent.Tests.Hub;

using Microsoft.Extensions.Logging.Abstractions;
using RPA.Agent.Hub;

public class HubConnectionStatusCoordinatorTests
{
    private static HubConnectionStatusCoordinator Create() => new(NullLogger<HubConnectionStatusCoordinator>.Instance);

    [Fact]
    public void Baslangic_Durumu_Offline()
    {
        var coordinator = Create();
        Assert.Equal(ConnectionStatus.Offline, coordinator.Status);
    }

    [Fact]
    public void OnConnected_Online_Yapar_Ve_Olay_Tetikler()
    {
        var coordinator = Create();
        ConnectionStatus? seen = null;
        coordinator.StatusChanged += s => seen = s;

        coordinator.OnConnected();

        Assert.Equal(ConnectionStatus.Online, coordinator.Status);
        Assert.Equal(ConnectionStatus.Online, seen);
    }

    [Fact]
    public void OnReconnecting_Sonra_OnReconnected_Online_Doner()
    {
        var coordinator = Create();
        coordinator.OnConnected();

        coordinator.OnReconnecting(new InvalidOperationException("kopma"));
        Assert.Equal(ConnectionStatus.Reconnecting, coordinator.Status);

        coordinator.OnReconnected();
        Assert.Equal(ConnectionStatus.Online, coordinator.Status);
    }

    [Fact]
    public void OnClosed_Offline_Yapar()
    {
        var coordinator = Create();
        coordinator.OnConnected();

        coordinator.OnClosed(new Exception("bağlantı koptu"));

        Assert.Equal(ConnectionStatus.Offline, coordinator.Status);
    }

    [Fact]
    public void Ayni_Duruma_Tekrar_Gecis_Olay_Tetiklemez()
    {
        var coordinator = Create();
        coordinator.OnConnected();
        var raised = 0;
        coordinator.StatusChanged += _ => raised++;

        coordinator.OnConnected();

        Assert.Equal(0, raised);
    }
}

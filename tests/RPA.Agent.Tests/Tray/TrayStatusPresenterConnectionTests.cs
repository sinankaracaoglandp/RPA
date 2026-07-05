namespace RPA.Agent.Tests.Tray;

using RPA.Agent.Hub;
using RPA.Agent.State;
using RPA.Agent.Tray;

public class TrayStatusPresenterConnectionTests
{
    [Fact]
    public void Varsayilan_Baglanti_Durumu_Cevrimici_Metnini_Icerir()
    {
        var presenter = new TrayStatusPresenter(new AgentState());
        Assert.Contains("Çevrimiçi", presenter.GetTooltip());
    }

    [Fact]
    public void UpdateConnectionStatus_Offline_Tooltipte_Gorunur()
    {
        var presenter = new TrayStatusPresenter(new AgentState());

        presenter.UpdateConnectionStatus(ConnectionStatus.Offline);

        Assert.Equal("Çevrimdışı", presenter.GetConnectionLabel());
        Assert.Contains("Çevrimdışı", presenter.GetTooltip());
    }

    [Fact]
    public void CanStopJob_Calisan_Is_Yoksa_False()
    {
        var presenter = new TrayStatusPresenter(new AgentState());
        Assert.False(presenter.CanStopJob());
    }

    [Fact]
    public void CanStopJob_Calisan_Is_Varsa_True()
    {
        var state = new AgentState();
        state.SetCurrentJob(Guid.NewGuid());
        var presenter = new TrayStatusPresenter(state);

        Assert.True(presenter.CanStopJob());
    }

    [Fact]
    public void Menu_Basliklari_Bos_Degildir()
    {
        var presenter = new TrayStatusPresenter(new AgentState());
        Assert.False(string.IsNullOrWhiteSpace(presenter.GetStopJobCaption()));
        Assert.False(string.IsNullOrWhiteSpace(presenter.GetOpenJobListCaption()));
        Assert.False(string.IsNullOrWhiteSpace(presenter.GetExitCaption()));
    }
}

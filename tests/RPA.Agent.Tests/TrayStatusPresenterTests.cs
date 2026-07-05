namespace RPA.Agent.Tests;

using RPA.Agent.State;
using RPA.Agent.Tray;

public class TrayStatusPresenterTests
{
    [Fact]
    public void Tooltip_RobotId_Ve_Sayaclari_Icerir()
    {
        var state = new AgentState();
        state.SetRobotId(Guid.NewGuid());
        state.RecordJobCompleted();
        state.SetActivity(AgentActivity.Idle);
        var presenter = new TrayStatusPresenter(state);

        var tip = presenter.GetTooltip();

        Assert.Contains("RPA Robot", tip);
        Assert.Contains("Tamamlanan: 1", tip);
        Assert.Contains("Boşta", tip);
    }

    [Fact]
    public void Kayitsiz_Robot_Tooltip_Kayitsiz_Gosterir()
    {
        var presenter = new TrayStatusPresenter(new AgentState());
        Assert.Contains("kayıtsız", presenter.GetTooltip());
    }

    [Fact]
    public void TogglePause_Durumu_Degistirir_Ve_Caption_Guncellenir()
    {
        var state = new AgentState();
        var presenter = new TrayStatusPresenter(state);
        Assert.Equal("Duraklat", presenter.GetPauseResumeCaption());

        presenter.TogglePause();
        Assert.True(state.IsPaused);
        Assert.Equal("Devam Et", presenter.GetPauseResumeCaption());

        presenter.TogglePause();
        Assert.False(state.IsPaused);
    }
}

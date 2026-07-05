namespace RPA.Agent.Tests;

using RPA.Agent.State;

public class AgentStateTests
{
    [Fact]
    public void Yeni_Durum_Starting_Ve_Sifir_Sayac()
    {
        var state = new AgentState();
        Assert.Null(state.RobotId);
        Assert.Equal(AgentActivity.Starting, state.Activity);
        Assert.Equal(0, state.CompletedJobCount);
        Assert.Equal(0, state.FailedJobCount);
        Assert.False(state.IsPaused);
    }

    [Fact]
    public void RobotId_Ve_Sayaclar_Guncellenir()
    {
        var state = new AgentState();
        var id = Guid.NewGuid();
        state.SetRobotId(id);
        state.RecordJobCompleted();
        state.RecordJobCompleted();
        state.RecordJobFailed();

        Assert.Equal(id, state.RobotId);
        Assert.Equal(2, state.CompletedJobCount);
        Assert.Equal(1, state.FailedJobCount);
    }

    [Fact]
    public void Pause_Aktivite_Paused_Yapar()
    {
        var state = new AgentState();
        state.SetPaused(true);
        Assert.True(state.IsPaused);
        Assert.Equal(AgentActivity.Paused, state.Activity);

        state.SetPaused(false);
        Assert.False(state.IsPaused);
        Assert.Equal(AgentActivity.Idle, state.Activity);
    }

    [Fact]
    public void Sayaclar_Es_Zamanli_Artislarda_Tutarli()
    {
        var state = new AgentState();
        Parallel.For(0, 1000, _ => state.RecordJobCompleted());
        Assert.Equal(1000, state.CompletedJobCount);
    }
}

namespace RPA.Agent.Tests.Localization;

using RPA.Agent.Localization;

public class AgentStringsTests
{
    [Theory]
    [InlineData("Tray.PauseJob")]
    [InlineData("Tray.StopJob")]
    [InlineData("JobList.Title")]
    [InlineData("UserPrompt.Title")]
    public void Bilinen_Anahtar_Iki_Dilde_De_Farkli_Ve_Bos_Olmayan_Metin_Dondurur(string key)
    {
        var tr = AgentStrings.Get(key, AgentLanguage.Turkish);
        var en = AgentStrings.Get(key, AgentLanguage.English);

        Assert.False(string.IsNullOrWhiteSpace(tr));
        Assert.False(string.IsNullOrWhiteSpace(en));
        Assert.NotEqual(tr, en);
    }

    [Fact]
    public void Bilinmeyen_Anahtar_Icin_Anahtarin_Kendisi_Dondurulur()
    {
        Assert.Equal("Nonexistent.Key", AgentStrings.Get("Nonexistent.Key", AgentLanguage.English));
    }
}

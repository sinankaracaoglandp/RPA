namespace RPA.Agent.Tests.Authentication;

using RPA.Agent.Authentication;

/// <summary>
/// Ajan aktivasyon girisi (--activate). Task 5 AgentEnrollmentClient.ActivateAsync'i yazmis ama
/// hicbir yerden CAGIRMAMISTI (sifir cagiran) — aktivasyon kodu tuketilemedigi icin gercek bir
/// ajan hic aktive edilemiyordu. Bu ayristirici o girisi kurar.
/// </summary>
public class AgentCommandLineTests
{
    [Fact]
    public void TryGetActivationCode_WithSeparateValue_ReturnsCode()
    {
        Assert.True(AgentCommandLine.TryGetActivationCode(["--activate", "KOD-123"], out var code));
        Assert.Equal("KOD-123", code);
    }

    [Fact]
    public void TryGetActivationCode_WithEqualsForm_ReturnsCode()
    {
        Assert.True(AgentCommandLine.TryGetActivationCode(["--activate=KOD-456"], out var code));
        Assert.Equal("KOD-456", code);
    }

    [Fact]
    public void TryGetActivationCode_IsCaseInsensitiveAndIgnoresOtherArguments()
    {
        Assert.True(AgentCommandLine.TryGetActivationCode(
            ["--baska", "deger", "--Activate", "KOD-789"], out var code));
        Assert.Equal("KOD-789", code);
    }

    [Fact]
    public void TryGetActivationCode_WithoutFlag_ReturnsFalse()
    {
        Assert.False(AgentCommandLine.TryGetActivationCode([], out _));
        Assert.False(AgentCommandLine.TryGetActivationCode(["--baska", "deger"], out _));
    }

    /// <summary>Deger verilmemis --activate sessizce "normal baslat"a DUSMEMELI: operator kodu unutmustur.</summary>
    [Theory]
    [InlineData(new object[] { new[] { "--activate" } })]
    [InlineData(new object[] { new[] { "--activate", "" } })]
    [InlineData(new object[] { new[] { "--activate", "   " } })]
    [InlineData(new object[] { new[] { "--activate=" } })]
    public void TryGetActivationCode_WithMissingValue_Throws(string[] args)
    {
        var error = Assert.Throws<ArgumentException>(() => AgentCommandLine.TryGetActivationCode(args, out _));
        Assert.Contains("--activate", error.Message);
    }

    [Fact]
    public void IsActivationUi_WithFlag_ReturnsTrue()
    {
        Assert.True(AgentCommandLine.IsActivationUi(["--activate-ui"]));
        Assert.True(AgentCommandLine.IsActivationUi(["--baska", "--Activate-UI"]));
    }

    [Fact]
    public void IsActivationUi_WithoutFlag_ReturnsFalse()
    {
        Assert.False(AgentCommandLine.IsActivationUi([]));
        Assert.False(AgentCommandLine.IsActivationUi(["--activate", "KOD-123"]));
    }
}

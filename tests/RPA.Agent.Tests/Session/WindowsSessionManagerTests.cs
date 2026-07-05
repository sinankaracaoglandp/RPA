namespace RPA.Agent.Tests.Session;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RPA.Agent.Session;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;

public class WindowsSessionManagerTests
{
    private static SessionCredentialProvider CredentialProvider(
        Mock<ICredentialVault> vault, SessionManagerOptions options)
        => new(vault.Object, Options.Create(options));

    private static WindowsSessionManager Create(
        Mock<ISessionInfoProvider> info,
        Mock<IAutoLogonRegistry> registry,
        Mock<ISessionSwitcher> switcher,
        SessionManagerOptions options,
        Mock<ICredentialVault>? vault = null)
    {
        vault ??= new Mock<ICredentialVault>();
        return new WindowsSessionManager(
            info.Object, registry.Object, switcher.Object,
            CredentialProvider(vault, options), Options.Create(options),
            NullLogger<WindowsSessionManager>.Instance);
    }

    [Fact]
    public async Task Attended_Mod_Otomatik_Islem_Yapmaz()
    {
        var info = new Mock<ISessionInfoProvider>();
        var registry = new Mock<IAutoLogonRegistry>();
        var switcher = new Mock<ISessionSwitcher>();
        var mgr = Create(info, registry, switcher, new SessionManagerOptions { AllowAutoLogon = true });

        await mgr.EnsureSessionAsync(SessionMode.Attended);

        registry.Verify(r => r.Configure(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>()), Times.Never);
        switcher.Verify(s => s.SwitchToConsoleAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Unattended_AllowAutoLogon_Kapaliysa_Hata()
    {
        var mgr = Create(new Mock<ISessionInfoProvider>(), new Mock<IAutoLogonRegistry>(),
            new Mock<ISessionSwitcher>(), new SessionManagerOptions { AllowAutoLogon = false });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => mgr.EnsureSessionAsync(SessionMode.Unattended));
    }

    [Fact]
    public async Task Unattended_AutoLogon_Kurar_Parola_Vaulttan()
    {
        var vault = new Mock<ICredentialVault>();
        vault.Setup(v => v.GetSecretAsync("pw-key")).ReturnsAsync(new SecureString("s3cret"));
        var registry = new Mock<IAutoLogonRegistry>();
        string? capturedPw = null;
        registry.Setup(r => r.Configure("robot", "CORP", It.IsAny<string>()))
            .Callback<string, string?, string>((_, _, pw) => capturedPw = pw);
        var options = new SessionManagerOptions
        {
            AllowAutoLogon = true,
            AutoLogonUserName = "robot",
            AutoLogonDomain = "CORP",
            AutoLogonPasswordVaultKey = "pw-key"
        };

        var mgr = Create(new Mock<ISessionInfoProvider>(), registry, new Mock<ISessionSwitcher>(), options, vault);
        await mgr.EnsureSessionAsync(SessionMode.Unattended);

        registry.Verify(r => r.Configure("robot", "CORP", It.IsAny<string>()), Times.Once);
        Assert.Equal("s3cret", capturedPw);
    }

    [Fact]
    public async Task SwitchToConsole_Switchera_Delege_Eder()
    {
        var switcher = new Mock<ISessionSwitcher>();
        var mgr = Create(new Mock<ISessionInfoProvider>(), new Mock<IAutoLogonRegistry>(),
            switcher, new SessionManagerOptions());

        await mgr.SwitchToConsoleAsync(3);

        switcher.Verify(s => s.SwitchToConsoleAsync(3, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reconnect_Etkin_Oturumda_Gecis_Yapmaz()
    {
        var info = new Mock<ISessionInfoProvider>();
        info.Setup(i => i.GetActiveSession()).Returns(new SessionInfo(1, SessionState.Active, "robot"));
        var switcher = new Mock<ISessionSwitcher>();
        var mgr = Create(info, new Mock<IAutoLogonRegistry>(), switcher, new SessionManagerOptions());

        await mgr.ReconnectIfNeededAsync();

        switcher.Verify(s => s.SwitchToConsoleAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reconnect_Kopmus_Oturumu_Konsola_Tasir()
    {
        var info = new Mock<ISessionInfoProvider>();
        info.Setup(i => i.GetActiveSession()).Returns(new SessionInfo(-1, SessionState.Unknown, null));
        info.Setup(i => i.ListSessions()).Returns(new[]
        {
            new SessionInfo(0, SessionState.LoggedOff, null),
            new SessionInfo(2, SessionState.Disconnected, "robot")
        });
        var switcher = new Mock<ISessionSwitcher>();
        var mgr = Create(info, new Mock<IAutoLogonRegistry>(), switcher, new SessionManagerOptions());

        await mgr.ReconnectIfNeededAsync();

        switcher.Verify(s => s.SwitchToConsoleAsync(2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reconnect_Aday_Yoksa_Sessizce_Gecer()
    {
        var info = new Mock<ISessionInfoProvider>();
        info.Setup(i => i.GetActiveSession()).Returns(new SessionInfo(-1, SessionState.Unknown, null));
        info.Setup(i => i.ListSessions()).Returns(Array.Empty<SessionInfo>());
        var switcher = new Mock<ISessionSwitcher>();
        var mgr = Create(info, new Mock<IAutoLogonRegistry>(), switcher, new SessionManagerOptions());

        await mgr.ReconnectIfNeededAsync();

        switcher.Verify(s => s.SwitchToConsoleAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Rdp_Mod_EnsureSession_Reconnect_Cagirir()
    {
        var info = new Mock<ISessionInfoProvider>();
        info.Setup(i => i.GetActiveSession()).Returns(new SessionInfo(2, SessionState.Disconnected, "robot"));
        info.Setup(i => i.ListSessions()).Returns(new[] { new SessionInfo(2, SessionState.Disconnected, "robot") });
        var switcher = new Mock<ISessionSwitcher>();
        var mgr = Create(info, new Mock<IAutoLogonRegistry>(), switcher, new SessionManagerOptions());

        await mgr.EnsureSessionAsync(SessionMode.Rdp);

        switcher.Verify(s => s.SwitchToConsoleAsync(2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetActiveSession_Saglayicidan_Doner()
    {
        var info = new Mock<ISessionInfoProvider>();
        info.Setup(i => i.GetActiveSession()).Returns(new SessionInfo(5, SessionState.Active, "u"));
        var mgr = Create(info, new Mock<IAutoLogonRegistry>(), new Mock<ISessionSwitcher>(), new SessionManagerOptions());

        var result = await mgr.GetActiveSessionAsync();

        Assert.Equal(5, result.SessionId);
        Assert.Equal(SessionState.Active, result.State);
    }
}

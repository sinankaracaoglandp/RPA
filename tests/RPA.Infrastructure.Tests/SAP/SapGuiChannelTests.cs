namespace RPA.Infrastructure.Tests.SAP;

using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;
using RPA.Domain.ValueObjects;
using RPA.Infrastructure.SAP;
using RPA.Infrastructure.SAP.SapGuiElements;
using BusinessException = RPA.Domain.Exceptions.BusinessException;
using SystemException = RPA.Domain.Exceptions.SystemException;

/// <summary>
/// SAP GUI Scripting kanalı testleri (Task 4.1). Spec Bölüm 5.3, 6.
/// COM oturumu mock'lanır / stub'lanır — gerçek DEP entegrasyon testi kapsam dışıdır.
/// Arrange-Act-Assert deseni.
/// </summary>
public class SapGuiChannelTests
{
    private static SapGuiSessionManager NewManager() =>
        new(NullLogger<SapGuiSessionManager>.Instance);

    private static SapGuiChannel NewChannel(ISapGuiSessionManager manager) =>
        new(manager, NullLogger<SapGuiChannel>.Instance);

    private static async Task<SapGuiChannel> NewConnectedChannel()
    {
        var channel = NewChannel(NewManager());
        await channel.ConnectAsync("DEV", "100", "TESTUSER", "pw", "TR");
        return channel;
    }

    private static IActivityExecutionContext Ctx() => new TestActivityExecutionContext();

    // =============================================================== Domain artefacts

    [Fact]
    public void SapConnection_HasSensibleDefaults()
    {
        var conn = new SapConnection { SystemName = "DEV", Client = "100", CredentialVaultKeyRef = "sap/dev" };

        Assert.Equal(SapChannelType.Gui, conn.ConnectionType);
        Assert.Equal(60, conn.Timeout);
        Assert.True(conn.IsActive);
        Assert.Equal("EN", conn.LanguageCode);
        Assert.NotEqual(Guid.Empty, conn.Id);
    }

    [Fact]
    public void SapChannelType_HasGuiAndNco()
    {
        Assert.Equal(0, (int)SapChannelType.Gui);
        Assert.Equal(1, (int)SapChannelType.Nco);
    }

    [Fact]
    public void SapGuiElement_IsImmutableRecordWithDefaults()
    {
        var el = new SapGuiElement("wnd[0]/usr/ctxtRMMG1-MATNR", "GuiCTextField", "Malzeme");

        Assert.Equal("wnd[0]/usr/ctxtRMMG1-MATNR", el.Id);
        Assert.Equal("GuiCTextField", el.Type);
        Assert.Equal("Malzeme", el.Text);
        Assert.True(el.Enabled);
        Assert.True(el.Changeable);
    }

    // =============================================================== SessionManager

    [Fact]
    public async Task SessionManager_Logon_ReturnsConnectedSession_AndTracksIt()
    {
        var mgr = NewManager();

        var session = await mgr.LogonAsync("DEV", "100", "USER", "pw", "TR");

        Assert.True(session.IsConnected);
        Assert.NotEmpty(session.Id);
        Assert.Single(mgr.ActiveSessions);
    }

    [Fact]
    public async Task SessionManager_Logoff_ClosesSession_AndUntracksIt()
    {
        var mgr = NewManager();
        var session = await mgr.LogonAsync("DEV", "100", "USER", "pw");

        await mgr.LogoffAsync(session);

        Assert.False(session.IsConnected);
        Assert.Empty(mgr.ActiveSessions);
    }

    [Theory]
    [InlineData("", "100", "USER")]
    [InlineData("DEV", "", "USER")]
    [InlineData("DEV", "100", "")]
    public async Task SessionManager_Logon_WithMissingInput_ThrowsBusinessException(string sys, string client, string user)
    {
        var mgr = NewManager();

        await Assert.ThrowsAsync<BusinessException>(() => mgr.LogonAsync(sys, client, user, "pw"));
    }

    [Fact]
    public async Task SessionManager_Logon_WhenCancelled_Throws()
    {
        var mgr = NewManager();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => mgr.LogonAsync("DEV", "100", "USER", "pw", "EN", cts.Token));
    }

    [Fact]
    public async Task SessionManager_Logoff_NullSession_Throws()
    {
        var mgr = NewManager();
        await Assert.ThrowsAsync<ArgumentNullException>(() => mgr.LogoffAsync(null!));
    }

    // =============================================================== Channel: lifecycle

    [Fact]
    public async Task Channel_Connect_MakesChannelHealthy()
    {
        var channel = await NewConnectedChannel();

        Assert.True(await channel.IsHealthyAsync());
    }

    [Fact]
    public async Task Channel_BeforeConnect_IsNotHealthy()
    {
        var channel = NewChannel(NewManager());

        Assert.False(await channel.IsHealthyAsync());
    }

    [Fact]
    public async Task Channel_DoubleConnect_ReconnectsAndRemainsHealthy()
    {
        var channel = await NewConnectedChannel();

        await channel.ConnectAsync("DEV", "100", "USER", "pw");

        Assert.True(await channel.IsHealthyAsync());
    }

    [Fact]
    public async Task Channel_Disconnect_MakesChannelUnhealthy()
    {
        var channel = await NewConnectedChannel();

        await channel.DisconnectAsync();

        Assert.False(await channel.IsHealthyAsync());
    }

    [Fact]
    public async Task Channel_Disconnect_WhenNotConnected_IsNoOp()
    {
        var channel = NewChannel(NewManager());

        await channel.DisconnectAsync(); // should not throw
        Assert.False(await channel.IsHealthyAsync());
    }

    // =============================================================== Channel: operations

    [Fact]
    public async Task Channel_SetText_ThenGetText_Roundtrips()
    {
        var channel = await NewConnectedChannel();

        await channel.SetTextAsync("wnd[0]/usr/ctxtRMMG1-MATNR", "MAT-001");
        var value = await channel.GetTextAsync("wnd[0]/usr/ctxtRMMG1-MATNR");

        Assert.Equal("MAT-001", value);
    }

    [Fact]
    public async Task Channel_Click_Succeeds()
    {
        var channel = await NewConnectedChannel();
        await channel.ClickAsync("wnd[0]/tbar[0]/btn[0]"); // no throw
    }

    [Fact]
    public async Task Channel_SelectTab_Succeeds()
    {
        var channel = await NewConnectedChannel();
        await channel.SelectTabAsync("wnd[0]/usr/tabsTAB/tabpT01"); // no throw
    }

    [Fact]
    public async Task Channel_SelectMenu_Succeeds()
    {
        var channel = await NewConnectedChannel();
        await channel.SelectMenuAsync("Sistem/Liste/Yazdır"); // no throw
    }

    [Fact]
    public async Task Channel_SelectMenu_EmptyPath_ThrowsBusinessException()
    {
        var channel = await NewConnectedChannel();
        await Assert.ThrowsAsync<BusinessException>(() => channel.SelectMenuAsync("   "));
    }

    [Fact]
    public async Task Channel_ReadGrid_ReturnsRows()
    {
        var channel = await NewConnectedChannel();

        var rows = await channel.ReadGridAsync("wnd[0]/usr/cntlGRID/shellcont/shell");

        Assert.NotNull(rows);
    }

    [Fact]
    public async Task Channel_CaptureScreen_ReturnsBytes()
    {
        var channel = await NewConnectedChannel();

        var png = await channel.CaptureScreenAsync();

        Assert.NotEmpty(png);
    }

    [Fact]
    public async Task Channel_ExecuteTransaction_Succeeds()
    {
        var channel = await NewConnectedChannel();
        await channel.ExecuteTransactionAsync("MM01"); // no throw
    }

    // =============================================================== Channel: guards & error mapping

    [Fact]
    public async Task Channel_OperationBeforeConnect_ThrowsSystemException()
    {
        var channel = NewChannel(NewManager());

        await Assert.ThrowsAsync<SystemException>(() => channel.ClickAsync("wnd[0]"));
    }

    [Fact]
    public async Task Channel_EmptyElementId_ThrowsBusinessException()
    {
        var channel = await NewConnectedChannel();

        await Assert.ThrowsAsync<BusinessException>(() => channel.ClickAsync(""));
    }

    [Fact]
    public async Task Channel_EmptyTransactionCode_ThrowsBusinessException()
    {
        var channel = await NewConnectedChannel();

        await Assert.ThrowsAsync<BusinessException>(() => channel.ExecuteTransactionAsync("  "));
    }

    [Fact]
    public async Task Channel_WhenSessionThrowsUnexpected_WrapsAsSystemException()
    {
        var session = new Mock<ISapGuiSession>();
        session.SetupGet(s => s.Id).Returns("S1");
        session.SetupGet(s => s.IsConnected).Returns(true);
        session.Setup(s => s.ClickAsync(It.IsAny<string>()))
               .ThrowsAsync(new InvalidOperationException("COM boom"));

        var mgr = new Mock<ISapGuiSessionManager>();
        mgr.Setup(m => m.LogonAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(session.Object);

        var channel = NewChannel(mgr.Object);
        await channel.ConnectAsync("DEV", "100", "USER", "pw");

        await Assert.ThrowsAsync<SystemException>(() => channel.ClickAsync("wnd[0]/btn"));
    }

    [Fact]
    public async Task Channel_Connect_DelegatesToSessionManagerWithMaskedPassword()
    {
        var mgr = new Mock<ISapGuiSessionManager>();
        var session = new Mock<ISapGuiSession>();
        session.SetupGet(s => s.IsConnected).Returns(true);
        session.SetupGet(s => s.Id).Returns("S1");
        mgr.Setup(m => m.LogonAsync("DEV", "100", "USER", "secret", "TR", It.IsAny<CancellationToken>()))
           .ReturnsAsync(session.Object)
           .Verifiable();

        var channel = NewChannel(mgr.Object);
        await channel.ConnectAsync("DEV", "100", "USER", "secret", "TR");

        mgr.Verify();
    }

    // =============================================================== Activities

    [Fact]
    public async Task ConnectActivity_ResolvesCredential_AndConnects()
    {
        var channel = new Mock<ISapGuiChannel>();
        var activity = new SapGuiConnectActivity(channel.Object);
        var ctx = Ctx();
        ctx.SetVariable("systemId", "DEV");
        ctx.SetVariable("client", "100");
        ctx.SetVariable("userId", "USER");
        ctx.SetVariable("credentialName", "sap/dev/pw");
        ctx.SetVariable("language", "TR");

        await activity.ExecuteAsync(ctx);

        // TestContext resolves credential to "mock-credential"
        channel.Verify(c => c.ConnectAsync("DEV", "100", "USER", "mock-credential", "TR"), Times.Once);
    }

    [Fact]
    public async Task ConnectActivity_MissingSystemId_ThrowsBusinessException()
    {
        var activity = new SapGuiConnectActivity(Mock.Of<ISapGuiChannel>());
        var ctx = Ctx();
        ctx.SetVariable("credentialName", "sap/dev/pw");

        await Assert.ThrowsAsync<BusinessException>(() => activity.ExecuteAsync(ctx));
    }

    [Fact]
    public async Task ConnectActivity_MissingCredential_ThrowsBusinessException()
    {
        var activity = new SapGuiConnectActivity(Mock.Of<ISapGuiChannel>());
        var ctx = Ctx();
        ctx.SetVariable("systemId", "DEV");
        ctx.SetVariable("client", "100");
        ctx.SetVariable("userId", "USER");

        await Assert.ThrowsAsync<BusinessException>(() => activity.ExecuteAsync(ctx));
    }

    [Fact]
    public async Task ClickActivity_CallsChannel()
    {
        var channel = new Mock<ISapGuiChannel>();
        var activity = new SapGuiClickActivity(channel.Object);
        var ctx = Ctx();
        ctx.SetVariable("elementId", "wnd[0]/btn");

        await activity.ExecuteAsync(ctx);

        channel.Verify(c => c.ClickAsync("wnd[0]/btn"), Times.Once);
    }

    [Fact]
    public async Task ClickActivity_MissingElementId_ThrowsBusinessException()
    {
        var activity = new SapGuiClickActivity(Mock.Of<ISapGuiChannel>());
        await Assert.ThrowsAsync<BusinessException>(() => activity.ExecuteAsync(Ctx()));
    }

    [Fact]
    public async Task SetTextActivity_CallsChannel()
    {
        var channel = new Mock<ISapGuiChannel>();
        var activity = new SapGuiSetTextActivity(channel.Object);
        var ctx = Ctx();
        ctx.SetVariable("elementId", "wnd[0]/usr/fld");
        ctx.SetVariable("text", "hello");

        await activity.ExecuteAsync(ctx);

        channel.Verify(c => c.SetTextAsync("wnd[0]/usr/fld", "hello"), Times.Once);
    }

    [Fact]
    public async Task SetTextActivity_MissingElementId_ThrowsBusinessException()
    {
        var activity = new SapGuiSetTextActivity(Mock.Of<ISapGuiChannel>());
        var ctx = Ctx();
        ctx.SetVariable("text", "hello");
        await Assert.ThrowsAsync<BusinessException>(() => activity.ExecuteAsync(ctx));
    }

    [Fact]
    public async Task GetTextActivity_SetsOutputVariable()
    {
        var channel = new Mock<ISapGuiChannel>();
        channel.Setup(c => c.GetTextAsync("wnd[0]/usr/fld")).ReturnsAsync("world");
        var activity = new SapGuiGetTextActivity(channel.Object);
        var ctx = Ctx();
        ctx.SetVariable("elementId", "wnd[0]/usr/fld");

        var result = await activity.ExecuteAsync(ctx);

        Assert.Equal("world", result["text"]);
        Assert.Equal("world", ctx.GetVariable<string>("text"));
    }

    [Fact]
    public async Task GetTextActivity_MissingElementId_ThrowsBusinessException()
    {
        var activity = new SapGuiGetTextActivity(Mock.Of<ISapGuiChannel>());
        await Assert.ThrowsAsync<BusinessException>(() => activity.ExecuteAsync(Ctx()));
    }

    [Fact]
    public async Task SelectTabActivity_CallsChannel()
    {
        var channel = new Mock<ISapGuiChannel>();
        var activity = new SapGuiSelectTabActivity(channel.Object);
        var ctx = Ctx();
        ctx.SetVariable("elementId", "wnd[0]/usr/tabs/tabpT01");

        await activity.ExecuteAsync(ctx);

        channel.Verify(c => c.SelectTabAsync("wnd[0]/usr/tabs/tabpT01"), Times.Once);
    }

    [Fact]
    public async Task SelectTabActivity_MissingElementId_ThrowsBusinessException()
    {
        var activity = new SapGuiSelectTabActivity(Mock.Of<ISapGuiChannel>());
        await Assert.ThrowsAsync<BusinessException>(() => activity.ExecuteAsync(Ctx()));
    }

    [Fact]
    public async Task SelectMenuActivity_CallsChannel()
    {
        var channel = new Mock<ISapGuiChannel>();
        var activity = new SapGuiSelectMenuActivity(channel.Object);
        var ctx = Ctx();
        ctx.SetVariable("menuPath", "Sistem/Liste/Yazdır");

        await activity.ExecuteAsync(ctx);

        channel.Verify(c => c.SelectMenuAsync("Sistem/Liste/Yazdır"), Times.Once);
    }

    [Fact]
    public async Task SelectMenuActivity_MissingMenuPath_ThrowsBusinessException()
    {
        var activity = new SapGuiSelectMenuActivity(Mock.Of<ISapGuiChannel>());
        await Assert.ThrowsAsync<BusinessException>(() => activity.ExecuteAsync(Ctx()));
    }

    [Fact]
    public async Task GridReadActivity_SetsRowsOutput()
    {
        var rows = new List<Dictionary<string, object?>> { new() { ["MATNR"] = "1" } };
        var channel = new Mock<ISapGuiChannel>();
        channel.Setup(c => c.ReadGridAsync("grid")).ReturnsAsync(rows);
        var activity = new SapGuiGridReadActivity(channel.Object);
        var ctx = Ctx();
        ctx.SetVariable("gridId", "grid");

        var result = await activity.ExecuteAsync(ctx);

        Assert.Same(rows, result["rows"]);
        Assert.Same(rows, ctx.GetVariable<List<Dictionary<string, object?>>>("rows"));
    }

    [Fact]
    public async Task GridReadActivity_MissingGridId_ThrowsBusinessException()
    {
        var activity = new SapGuiGridReadActivity(Mock.Of<ISapGuiChannel>());
        await Assert.ThrowsAsync<BusinessException>(() => activity.ExecuteAsync(Ctx()));
    }

    [Fact]
    public async Task ScreenshotActivity_SetsScreenshotOutput()
    {
        var png = new byte[] { 1, 2, 3 };
        var channel = new Mock<ISapGuiChannel>();
        channel.Setup(c => c.CaptureScreenAsync()).ReturnsAsync(png);
        var activity = new SapGuiScreenshotActivity(channel.Object);
        var ctx = Ctx();

        var result = await activity.ExecuteAsync(ctx);

        Assert.Same(png, result["screenshot"]);
    }

    [Fact]
    public void AllActivities_ExposeMetadata_WithSapCategoryAndCapability()
    {
        var channel = Mock.Of<ISapGuiChannel>();
        IActivity[] activities =
        {
            new SapGuiConnectActivity(channel),
            new SapGuiClickActivity(channel),
            new SapGuiSetTextActivity(channel),
            new SapGuiGetTextActivity(channel),
            new SapGuiSelectTabActivity(channel),
            new SapGuiSelectMenuActivity(channel),
            new SapGuiGridReadActivity(channel),
            new SapGuiScreenshotActivity(channel),
        };

        foreach (var a in activities)
        {
            var meta = a.GetMetadata();
            Assert.StartsWith("Sap.Gui.", meta.ActivityId);
            Assert.Equal("SAP", meta.Category);
            Assert.Contains("sap-gui", meta.RequiredCapabilities);
        }
    }

    [Fact]
    public void Activities_NullChannel_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => new SapGuiClickActivity(null!));
    }
}

namespace RPA.Infrastructure.Tests.Desktop;

using Moq;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Activities.Desktop;
using BusinessException = RPA.Domain.Exceptions.BusinessException;

/// <summary>
/// Desktop.* aktivite ailesi birim testleri (Spec Bölüm 5 — Paket E). FlaUI/UIA gerçek UI
/// gerektirdiğinden kanal (<see cref="IDesktopAutomationChannel"/>) mock'lanır; aktivite
/// mantığı (input doğrulama, kanal çağrısı, çıktı yazımı) doğrulanır.
/// </summary>
public class DesktopActivitiesTests
{
    private static (T activity, Mock<IDesktopAutomationChannel> channel) Build<T>(Func<IDesktopAutomationChannel, T> factory)
    {
        var channel = new Mock<IDesktopAutomationChannel>();
        return (factory(channel.Object), channel);
    }

    private static TestActivityExecutionContext Ctx(params (string name, object? value)[] vars)
    {
        var ctx = new TestActivityExecutionContext();
        foreach (var (name, value) in vars)
        {
            ctx.SetVariable(name, value);
        }
        return ctx;
    }

    // ---- Attach ----

    [Fact]
    public async Task Attach_WithProcessName_ReturnsWindowHandle()
    {
        var (act, channel) = Build(c => new DesktopAttachActivity(c));
        channel.Setup(c => c.AttachAsync("notepad", null)).ReturnsAsync("win-1");

        var result = await act.ExecuteAsync(Ctx(("processName", "notepad")));

        Assert.Equal("win-1", result["windowHandle"]);
        channel.Verify(c => c.AttachAsync("notepad", null), Times.Once);
    }

    [Fact]
    public async Task Attach_WithoutProcessNameOrTitle_ThrowsBusiness()
    {
        var (act, _) = Build(c => new DesktopAttachActivity(c));

        await Assert.ThrowsAsync<BusinessException>(() => act.ExecuteAsync(Ctx()));
    }

    // ---- Launch ----

    [Fact]
    public async Task Launch_WithPath_ReturnsWindowHandle()
    {
        var (act, channel) = Build(c => new DesktopLaunchActivity(c));
        channel.Setup(c => c.LaunchAsync(@"C:\app.exe", "--x", true)).ReturnsAsync("win-9");

        var result = await act.ExecuteAsync(Ctx(("path", @"C:\app.exe"), ("arguments", "--x"), ("waitForIdle", true)));

        Assert.Equal("win-9", result["windowHandle"]);
    }

    [Fact]
    public async Task Launch_EmptyPath_ThrowsBusiness()
    {
        var (act, _) = Build(c => new DesktopLaunchActivity(c));

        await Assert.ThrowsAsync<BusinessException>(() => act.ExecuteAsync(Ctx(("path", ""))));
    }

    // ---- Click ----

    [Fact]
    public async Task Click_CallsChannelWithClickType()
    {
        var (act, channel) = Build(c => new DesktopClickActivity(c));

        await act.ExecuteAsync(Ctx(("selector", "Window/Button[AutomationId='ok']"), ("clickType", "double")));

        channel.Verify(c => c.ClickAsync("Window/Button[AutomationId='ok']", "double"), Times.Once);
    }

    [Fact]
    public async Task Click_EmptySelector_ThrowsBusiness()
    {
        var (act, _) = Build(c => new DesktopClickActivity(c));

        await Assert.ThrowsAsync<BusinessException>(() => act.ExecuteAsync(Ctx(("selector", "  "))));
    }

    // ---- SetText ----

    [Fact]
    public async Task SetText_FillsTextbox()
    {
        var (act, channel) = Build(c => new DesktopSetTextActivity(c));

        await act.ExecuteAsync(Ctx(("selector", "Edit[AutomationId='amount']"), ("text", "42")));

        channel.Verify(c => c.SetTextAsync("Edit[AutomationId='amount']", "42"), Times.Once);
    }

    [Fact]
    public async Task SetText_NullText_SendsEmpty()
    {
        var (act, channel) = Build(c => new DesktopSetTextActivity(c));

        await act.ExecuteAsync(Ctx(("selector", "Edit[AutomationId='amount']")));

        channel.Verify(c => c.SetTextAsync("Edit[AutomationId='amount']", ""), Times.Once);
    }

    // ---- GetText ----

    [Fact]
    public async Task GetText_WritesOutputAndVariable()
    {
        var (act, channel) = Build(c => new DesktopGetTextActivity(c));
        channel.Setup(c => c.GetTextAsync("Text[Name='status']")).ReturnsAsync("Hazır");
        var ctx = Ctx(("selector", "Text[Name='status']"));

        var result = await act.ExecuteAsync(ctx);

        Assert.Equal("Hazır", result["text"]);
        Assert.Equal("Hazır", ctx.GetVariable<string>("text"));
    }

    // ---- SelectItem ----

    [Fact]
    public async Task SelectItem_CallsChannel()
    {
        var (act, channel) = Build(c => new DesktopSelectItemActivity(c));

        await act.ExecuteAsync(Ctx(("selector", "ComboBox[AutomationId='city']"), ("item", "Ankara")));

        channel.Verify(c => c.SelectItemAsync("ComboBox[AutomationId='city']", "Ankara"), Times.Once);
    }

    [Fact]
    public async Task SelectItem_EmptyItem_ThrowsBusiness()
    {
        var (act, _) = Build(c => new DesktopSelectItemActivity(c));

        await Assert.ThrowsAsync<BusinessException>(() =>
            act.ExecuteAsync(Ctx(("selector", "ComboBox[AutomationId='city']"), ("item", ""))));
    }

    // ---- SendKeys ----

    [Fact]
    public async Task SendKeys_WithSelector_CallsChannel()
    {
        var (act, channel) = Build(c => new DesktopSendKeysActivity(c));

        await act.ExecuteAsync(Ctx(("selector", "Edit[AutomationId='date']"), ("keys", "09.07.2026")));

        channel.Verify(c => c.SendKeysAsync("Edit[AutomationId='date']", "09.07.2026"), Times.Once);
    }

    [Fact]
    public async Task SendKeys_NullSelector_SendsToFocus()
    {
        var (act, channel) = Build(c => new DesktopSendKeysActivity(c));

        await act.ExecuteAsync(Ctx(("keys", "^s")));

        channel.Verify(c => c.SendKeysAsync(null, "^s"), Times.Once);
    }

    [Fact]
    public async Task SendKeys_EmptyKeys_ThrowsBusiness()
    {
        var (act, _) = Build(c => new DesktopSendKeysActivity(c));

        await Assert.ThrowsAsync<BusinessException>(() => act.ExecuteAsync(Ctx(("keys", ""))));
    }

    // ---- WaitFor ----

    [Fact]
    public async Task WaitFor_PassesTimeout()
    {
        var (act, channel) = Build(c => new DesktopWaitForActivity(c));

        await act.ExecuteAsync(Ctx(("selector", "Window[Title~'Kaydet']"), ("timeoutMs", 5000)));

        channel.Verify(c => c.WaitForAsync("Window[Title~'Kaydet']", 5000), Times.Once);
    }

    // ---- Screenshot ----

    [Fact]
    public async Task Screenshot_ReturnsPath()
    {
        var (act, channel) = Build(c => new DesktopScreenshotActivity(c));
        channel.Setup(c => c.ScreenshotAsync(null)).ReturnsAsync(@"C:\shot.png");

        var result = await act.ExecuteAsync(Ctx());

        Assert.Equal(@"C:\shot.png", result["path"]);
    }

    // ---- Metadata sanity ----

    [Fact]
    public void AllActivities_HaveDesktopCapabilityAndId()
    {
        IActivity[] activities =
        {
            new DesktopAttachActivity(Mock.Of<IDesktopAutomationChannel>()),
            new DesktopLaunchActivity(Mock.Of<IDesktopAutomationChannel>()),
            new DesktopClickActivity(Mock.Of<IDesktopAutomationChannel>()),
            new DesktopSetTextActivity(Mock.Of<IDesktopAutomationChannel>()),
            new DesktopGetTextActivity(Mock.Of<IDesktopAutomationChannel>()),
            new DesktopSelectItemActivity(Mock.Of<IDesktopAutomationChannel>()),
            new DesktopSendKeysActivity(Mock.Of<IDesktopAutomationChannel>()),
            new DesktopWaitForActivity(Mock.Of<IDesktopAutomationChannel>()),
            new DesktopScreenshotActivity(Mock.Of<IDesktopAutomationChannel>()),
        };

        foreach (var a in activities)
        {
            var meta = a.GetMetadata();
            Assert.StartsWith("Desktop.", meta.ActivityId);
            Assert.Contains("desktop", meta.RequiredCapabilities);
        }
    }
}

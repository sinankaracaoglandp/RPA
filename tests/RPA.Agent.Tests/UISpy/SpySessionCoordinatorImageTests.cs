namespace RPA.Agent.Tests.UISpy;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RPA.Agent.UISpy;
using RPA.Infrastructure.UISpy;
using Xunit;

public class SpySessionCoordinatorImageTests
{
    [Fact]
    public async Task Start_ImageKind_SendsImageMessage()
    {
        var sessionId = Guid.NewGuid();
        var transport = new Mock<ISpyElementTransport>();
        var imagePicker = new Mock<IImageRegionPicker>();
        imagePicker.Setup(p => p.DetectOnceAsync(It.IsAny<ImagePickerOptions>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new ImagePick("BASE64", null));
        var options = Options.Create(new SpySessionOptions { TimeoutSeconds = 5 });

        var coordinator = new SpySessionCoordinator(
            Mock.Of<ISapGuiSinglePicker>(), transport.Object, options,
            NullLogger<SpySessionCoordinator>.Instance,
            imagePicker: imagePicker.Object);

        await coordinator.StartAsync(sessionId, "image", "{\"captureMode\":\"timer\",\"delaySeconds\":8}");

        transport.Verify(t => t.SendAsync(
            It.Is<SpyElementMessage>(m => m.Kind == "image" && m.ImageBase64 == "BASE64"),
            It.IsAny<CancellationToken>()), Times.Once);
        // Studio'dan gelen freeze seçenekleri parse edilip picker'a iletilmeli.
        imagePicker.Verify(p => p.DetectOnceAsync(
            It.Is<ImagePickerOptions>(o => o.CaptureMode == "timer" && o.DelaySeconds == 8),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void ImagePickerOptions_Parse_DefaultsToF2()
    {
        var def = ImagePickerOptions.Parse(null);
        Assert.Equal("f2", def.CaptureMode);
        Assert.Equal("F2", def.HotKey);
        Assert.Equal(0x71u, def.VirtualKey); // F2

        var bad = ImagePickerOptions.Parse("not-json");
        Assert.Equal("f2", bad.CaptureMode);

        var timer = ImagePickerOptions.Parse("{\"captureMode\":\"timer\",\"delaySeconds\":200}");
        Assert.Equal("timer", timer.CaptureMode);
        Assert.Equal(120, timer.DelaySeconds); // clamp 1..120
    }

    [Fact]
    public void ImagePickerOptions_Parse_HotKeyAndModifiers()
    {
        var opt = ImagePickerOptions.Parse("{\"captureMode\":\"f2\",\"hotKey\":\"F9\",\"ctrl\":true,\"shift\":true}");
        Assert.Equal("F9", opt.HotKey);
        Assert.Equal(0x78u, opt.VirtualKey);   // F9
        Assert.Equal(2u | 4u, opt.Modifiers);  // Ctrl(2) + Shift(4)
        Assert.Equal("Ctrl+Shift+F9", opt.DisplayCombo);

        // Geçersiz tuş → varsayılan F2.
        var bad = ImagePickerOptions.Parse("{\"hotKey\":\"F42\"}");
        Assert.Equal("F2", bad.HotKey);
    }

    // =============================================================== Onay tuşu: CapsLock

    [Theory]
    [InlineData("CapsLock")]
    [InlineData("capslock")]
    [InlineData("Caps")]
    [InlineData("CAPS_LOCK")]
    public void Parse_AcceptsCapsLock_AsConfirmKey(string key)
    {
        // SAP'ta F1–F12'nin tamamı transaction kısayoludur; Caps Lock hiçbir SAP fonksiyonunu
        // tetiklemediği için SAP picker'ının önerilen onay tuşudur.
        var options = ImagePickerOptions.Parse($$"""{"hotKey":"{{key}}"}""");

        Assert.Equal(ImagePickerOptions.CapsLockKey, options.HotKey);
        Assert.Equal(0x14u, options.VirtualKey); // VK_CAPITAL
        Assert.Equal("CapsLock", options.DisplayCombo);
    }

    [Fact]
    public void Parse_CapsLockWithModifiers_KeepsBothInDisplayCombo()
    {
        var options = ImagePickerOptions.Parse("""{"hotKey":"CapsLock","ctrl":true}""");

        Assert.Equal(0x14u, options.VirtualKey);
        Assert.Equal("Ctrl+CapsLock", options.DisplayCombo);
    }

    [Fact]
    public void Parse_FunctionKeysStillWork()
    {
        var options = ImagePickerOptions.Parse("""{"hotKey":"F8"}""");

        Assert.Equal("F8", options.HotKey);
        Assert.Equal(0x77u, options.VirtualKey); // F1=0x70 → F8=0x77
    }

    [Fact]
    public void Parse_UnknownKey_FallsBackToDefault()
    {
        var options = ImagePickerOptions.Parse("""{"hotKey":"Tab"}""");

        Assert.Equal(ImagePickerOptions.DefaultHotKey, options.HotKey);
    }

    [Theory]
    [InlineData("T", 0x54u)]
    [InlineData("t", 0x54u)]
    [InlineData("A", 0x41u)]
    [InlineData("Z", 0x5Au)]
    public void Parse_AcceptsLetterKeys(string key, uint expectedVirtualKey)
    {
        // SAP'ta F1–F12 doludur; harf + modifier kombinasyonları (Ctrl+T) serbesttir.
        var options = ImagePickerOptions.Parse($$"""{"hotKey":"{{key}}","ctrl":true}""");

        Assert.Equal(key.ToUpperInvariant(), options.HotKey);
        Assert.Equal(expectedVirtualKey, options.VirtualKey);
        Assert.Equal($"Ctrl+{key.ToUpperInvariant()}", options.DisplayCombo);
    }

    [Theory]
    [InlineData("TT")]   // iki harf
    [InlineData("1")]    // rakam
    [InlineData("+")]
    public void Parse_RejectsNonLetterSingleTokens(string key)
    {
        var options = ImagePickerOptions.Parse($$"""{"hotKey":"{{key}}"}""");

        Assert.Equal(ImagePickerOptions.DefaultHotKey, options.HotKey);
    }
}

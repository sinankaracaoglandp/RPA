namespace RPA.Infrastructure.Tests.UISpy;

using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RPA.Domain.ValueObjects;
using RPA.Infrastructure.UISpy;

/// <summary>
/// UI Spy tespit + gönderim testleri (Task 4.4, Spec Bölüm 6). Windows P/Invoke (INativeWindowApi)
/// ve SAP COM (ISapGuiElementResolver) mock'lanır; gerçek DEP entegrasyonu kapsam dışıdır.
/// Arrange-Act-Assert deseni.
/// </summary>
public class SapGuiElementDetectorTests
{
    private static SapGuiElementDetector NewDetector(
        Mock<INativeWindowApi> native,
        Mock<ISapGuiElementResolver> resolver)
        => new(native.Object, resolver.Object, NullLogger<SapGuiElementDetector>.Instance);

    // =============================================================== IsSapWindow

    [Theory]
    [InlineData("SAP_FRONTEND_SESSION", true)]
    [InlineData("SAP_FRONTEND_MAINWINDOW", true)]
    [InlineData("sap_frontend_session", true)] // case-insensitive
    [InlineData("Chrome_WidgetWin_1", false)]
    [InlineData("Notepad", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSapWindow_ClassifiesByPrefix(string? windowClass, bool expected)
    {
        Assert.Equal(expected, SapGuiElementDetector.IsSapWindow(windowClass));
    }

    // =============================================================== Kök pencere tespiti

    [Fact]
    public void DetectAt_WhenChildControlIsNotSapButRootIs_StillResolves()
    {
        // Gerçek dünya: WindowFromPoint SAP metin alanının üzerinde ALT kontrolü döndürür ve onun
        // sınıfı SAP_FRONTEND* DEĞİLDİR. Yalnız child sınıfına bakıldığında picker hiçbir zaman
        // element üretmiyordu (çerçeve yok, tıklama işlemiyor).
        var native = new Mock<INativeWindowApi>();
        native.Setup(n => n.GetWindowClassAt(120, 340)).Returns("Edit");
        native.Setup(n => n.GetRootWindowClassAt(120, 340)).Returns("SAP_FRONTEND_SESSION");

        var resolver = new Mock<ISapGuiElementResolver>();
        resolver.Setup(r => r.ResolveAt(120, 340))
                .Returns(new SapGuiElement("wnd[0]/usr/ctxtRMMG1-MATNR", "GuiCTextField", "Malzeme"));

        var element = NewDetector(native, resolver).DetectElementAt(120, 340);

        Assert.NotNull(element);
        Assert.Equal("wnd[0]/usr/ctxtRMMG1-MATNR", element!.Id);
    }

    [Fact]
    public void DetectAt_AlwaysAsksResolver_EvenWhenWindowClassLooksNonSap()
    {
        // Regresyon: pencere sınıfı kapısı SÜREKLİ yanlış negatif üretiyordu. WindowFromPoint en
        // derin child'ı verir; SAP ekranında bu 'Edit', açık menüde '#32768' olur — hiçbiri
        // 'SAP_FRONTEND*' değildir. Sınıfa bakıp erken dönmek picker'ı tamamen ölü bırakıyordu.
        // Otorite SAP'tır: FindByPosition bir şey döndürüyorsa nokta SAP oturumundadır.
        var native = new Mock<INativeWindowApi>();
        native.Setup(n => n.GetWindowClassAt(10, 10)).Returns("#32768"); // Windows menü sınıfı
        native.Setup(n => n.GetRootWindowClassAt(10, 10)).Returns("#32768");

        var resolver = new Mock<ISapGuiElementResolver>();
        resolver.Setup(r => r.ResolveAt(10, 10))
                .Returns(new SapGuiElement("wnd[0]/usr/ctxtRMMG1-MATNR", "GuiCTextField", "Malzeme"));

        var element = NewDetector(native, resolver).DetectElementAt(10, 10);

        Assert.NotNull(element);
        Assert.Equal("wnd[0]/usr/ctxtRMMG1-MATNR", element!.Id);
    }

    [Fact]
    public void DetectAt_WhenResolverFindsNothing_ReturnsNull()
    {
        var native = new Mock<INativeWindowApi>();
        native.Setup(n => n.GetWindowClassAt(10, 10)).Returns("Chrome_WidgetWin_1");
        native.Setup(n => n.GetRootWindowClassAt(10, 10)).Returns("Chrome_WidgetWin_1");

        var resolver = new Mock<ISapGuiElementResolver>(); // ResolveAt → null

        Assert.Null(NewDetector(native, resolver).DetectElementAt(10, 10));
    }

    // =============================================================== Diagnose (tanılama)

    [Fact]
    public void Diagnose_WhenNotSapWindow_ReportsBothClassNames()
    {
        var native = new Mock<INativeWindowApi>();
        native.Setup(n => n.GetWindowClassAt(5, 6)).Returns("Chrome_WidgetWin_1");
        native.Setup(n => n.GetRootWindowClassAt(5, 6)).Returns("Chrome_WidgetWin_0");

        var reason = NewDetector(native, new Mock<ISapGuiElementResolver>()).Diagnose(5, 6);

        Assert.Contains("SAP elementi yok", reason);
        Assert.Contains("Chrome_WidgetWin_1", reason);
        Assert.Contains("Chrome_WidgetWin_0", reason);
    }

    [Fact]
    public void Diagnose_WhenSapWindowButResolverFailed_ReportsResolverError()
    {
        var native = new Mock<INativeWindowApi>();
        native.Setup(n => n.GetWindowClassAt(5, 6)).Returns("Edit");
        native.Setup(n => n.GetRootWindowClassAt(5, 6)).Returns("SAP_FRONTEND_SESSION");

        var resolver = new Mock<ISapGuiElementResolver>();
        resolver.SetupGet(r => r.LastError).Returns("SystemException: SAP GUI Scripting devre dışı");

        var reason = NewDetector(native, resolver).Diagnose(5, 6);

        Assert.Contains("SAP GUI Scripting devre dışı", reason);
    }

    [Fact]
    public void Diagnose_WhenSapWindowAndNoResolverError_PointsAtScripting()
    {
        var native = new Mock<INativeWindowApi>();
        native.Setup(n => n.GetWindowClassAt(5, 6)).Returns("SAP_FRONTEND_SESSION");
        native.Setup(n => n.GetRootWindowClassAt(5, 6)).Returns("SAP_FRONTEND_SESSION");

        var reason = NewDetector(native, new Mock<ISapGuiElementResolver>()).Diagnose(5, 6);

        Assert.Contains("SAP elementi yok", reason);
    }

    // =============================================================== DetectElementUnderCursor

    [Fact]
    public void DetectUnderCursor_WhenSapWindow_ReturnsElementWithCursorPosition()
    {
        var native = new Mock<INativeWindowApi>();
        native.Setup(n => n.GetCursorPosition()).Returns((120, 340));
        native.Setup(n => n.GetWindowClassAt(120, 340)).Returns("SAP_FRONTEND_SESSION");

        var resolver = new Mock<ISapGuiElementResolver>();
        resolver.Setup(r => r.ResolveAt(120, 340))
                .Returns(new SapGuiElement("wnd[0]/usr/ctxtRMMG1-MATNR", "GuiCTextField", "Malzeme"));

        var element = NewDetector(native, resolver).DetectElementUnderCursor();

        Assert.NotNull(element);
        Assert.Equal("wnd[0]/usr/ctxtRMMG1-MATNR", element!.Id);
        Assert.Equal("GuiCTextField", element.Type);
        Assert.Equal(120, element.X);
        Assert.Equal(340, element.Y);
    }

    [Fact]
    public void DetectUnderCursor_WhenResolverFindsNothing_ReturnsNull()
    {
        // Pencere sınıfı artık kapı DEĞİLDİR (yanlış negatif kaynağıydı); sonucu SAP belirler.
        var native = new Mock<INativeWindowApi>();
        native.Setup(n => n.GetCursorPosition()).Returns((10, 20));
        native.Setup(n => n.GetWindowClassAt(10, 20)).Returns("Chrome_WidgetWin_1");

        var resolver = new Mock<ISapGuiElementResolver>(); // ResolveAt → null

        Assert.Null(NewDetector(native, resolver).DetectElementUnderCursor());
    }

    [Fact]
    public void DetectAt_WhenSapWindowButResolverReturnsNull_ReturnsNull()
    {
        var native = new Mock<INativeWindowApi>();
        native.Setup(n => n.GetWindowClassAt(5, 5)).Returns("SAP_FRONTEND_SESSION");

        var resolver = new Mock<ISapGuiElementResolver>();
        resolver.Setup(r => r.ResolveAt(5, 5)).Returns((SapGuiElement?)null);

        var element = NewDetector(native, resolver).DetectElementAt(5, 5);

        Assert.Null(element);
    }

    [Fact]
    public void DetectAt_ElementIdIsHierarchical()
    {
        var native = new Mock<INativeWindowApi>();
        native.Setup(n => n.GetWindowClassAt(1, 1)).Returns("SAP_FRONTEND_SESSION");
        var resolver = new Mock<ISapGuiElementResolver>();
        resolver.Setup(r => r.ResolveAt(1, 1))
                .Returns(new SapGuiElement("wnd[0]/usr/btn[OK]", "GuiButton", "OK"));

        var element = NewDetector(native, resolver).DetectElementAt(1, 1);

        Assert.NotNull(element);
        Assert.StartsWith("wnd[0]/", element!.Id);
        Assert.Contains("btn[OK]", element.Id);
    }

    [Fact]
    public void Constructor_NullDependencies_Throw()
    {
        var native = Mock.Of<INativeWindowApi>();
        var resolver = Mock.Of<ISapGuiElementResolver>();
        Assert.Throws<ArgumentNullException>(() => new SapGuiElementDetector(null!, resolver, NullLogger<SapGuiElementDetector>.Instance));
        Assert.Throws<ArgumentNullException>(() => new SapGuiElementDetector(native, null!, NullLogger<SapGuiElementDetector>.Instance));
    }

    [Fact]
    public void NullResolver_AlwaysReturnsNull()
    {
        var resolver = new NullSapGuiElementResolver();
        Assert.Null(resolver.ResolveAt(1, 2));
    }

    // =============================================================== Sender + message

    [Fact]
    public void SpyElementMessage_From_MapsAllFields()
    {
        var el = new SapGuiElement("wnd[0]/usr/btn[OK]", "GuiButton", "OK") { X = 7, Y = 9 };

        var msg = SpyElementMessage.From(el);

        Assert.Equal("wnd[0]/usr/btn[OK]", msg.ElementId);
        Assert.Equal("GuiButton", msg.Type);
        Assert.Equal("OK", msg.Text);
        Assert.Equal(7, msg.X);
        Assert.Equal(9, msg.Y);
        Assert.True(msg.Enabled);
        Assert.True(msg.Changeable);
    }

    [Fact]
    public void SpyElementMessage_FromWithSession_MapsSessionAndKind()
    {
        var sessionId = Guid.NewGuid();
        var el = new SapGuiElement("wnd[0]/usr/btn[OK]", "GuiButton", "OK") { X = 7, Y = 9 };

        var msg = SpyElementMessage.From(el, sessionId);

        Assert.Equal(sessionId, msg.SessionId);
        Assert.Equal("sap", msg.Kind);
        Assert.Equal("wnd[0]/usr/btn[OK]", msg.ElementId);
    }

    [Fact]
    public void SpyElementMessage_DefaultFrom_IsBackwardCompatibleSapMessage()
    {
        var el = new SapGuiElement("wnd[0]/usr/btn[OK]", "GuiButton", "OK");

        var msg = SpyElementMessage.From(el);

        Assert.Equal(Guid.Empty, msg.SessionId);
        Assert.Equal("sap", msg.Kind);
    }

    [Fact]
    public void SpyElementMessage_SerializesSessionAndKind()
    {
        var sessionId = Guid.NewGuid();
        var msg = new SpyElementMessage
        {
            SessionId = sessionId,
            Kind = "sap",
            ElementId = "wnd[0]/usr/btn[OK]",
            Type = "GuiButton",
        };

        var json = JsonSerializer.Serialize(msg);

        Assert.Contains(nameof(SpyElementMessage.SessionId), json);
        Assert.Contains(sessionId.ToString(), json);
        Assert.Contains(nameof(SpyElementMessage.Kind), json);
        Assert.Contains("sap", json);
    }

    [Fact]
    public async Task Sender_Send_FormatsAndForwardsToTransport()
    {
        var transport = new Mock<ISpyElementTransport>();
        SpyElementMessage? captured = null;
        transport.Setup(t => t.SendAsync(It.IsAny<SpyElementMessage>(), It.IsAny<CancellationToken>()))
                 .Callback<SpyElementMessage, CancellationToken>((m, _) => captured = m)
                 .Returns(Task.CompletedTask);

        var sender = new SapGuiElementSender(transport.Object, NullLogger<SapGuiElementSender>.Instance);
        var el = new SapGuiElement("wnd[0]/usr/fld", "GuiTextField", "x") { X = 3, Y = 4 };

        await sender.SendAsync(el);

        transport.Verify(t => t.SendAsync(It.IsAny<SpyElementMessage>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(captured);
        Assert.Equal("wnd[0]/usr/fld", captured!.ElementId);
        Assert.Equal(3, captured.X);
    }

    [Fact]
    public async Task Sender_NullElement_Throws()
    {
        var sender = new SapGuiElementSender(Mock.Of<ISpyElementTransport>(), NullLogger<SapGuiElementSender>.Instance);
        await Assert.ThrowsAsync<ArgumentNullException>(() => sender.SendAsync(null!));
    }

    [Fact]
    public void Sender_NullTransport_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new SapGuiElementSender(null!, NullLogger<SapGuiElementSender>.Instance));
    }
}

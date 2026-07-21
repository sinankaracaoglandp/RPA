namespace RPA.Infrastructure.Tests.UISpy;

using RPA.Infrastructure.UISpy;

/// <summary>
/// COM SAP element çözücüsünün COM'suz test edilebilir kısmı: SAP'ın döndürdüğü mutlak element
/// ID'sinin, aktivitelerin (<c>Sap.Gui.*</c> / <c>findById</c>) beklediği oturumdan bağımsız göreli
/// forma indirgenmesi. Gerçek COM yolu (FindByPosition) SAP GUI kurulu makinede doğrulanır.
/// </summary>
public class SapElementIdTests
{
    [Theory]
    // SAP'ın gerçek döndürdüğü mutlak form — bağlantı/oturum indeksi atılmalı.
    [InlineData("/app/con[0]/ses[0]/wnd[0]/usr/ctxtRMMG1-MATNR", "wnd[0]/usr/ctxtRMMG1-MATNR")]
    // Farklı bağlantı/oturum indeksi de aynı göreli ID'yi vermeli (tasarım anındaki oturum
    // çalışma anındakinden farklı olabilir).
    [InlineData("/app/con[2]/ses[3]/wnd[0]/usr/txtRF05A-NEWBS", "wnd[0]/usr/txtRF05A-NEWBS")]
    // Popup penceresi.
    [InlineData("/app/con[0]/ses[0]/wnd[1]/usr/btnBUTTON_1", "wnd[1]/usr/btnBUTTON_1")]
    // Zaten göreli — dokunulmamalı.
    [InlineData("wnd[0]/usr/ctxtRMMG1-MATNR", "wnd[0]/usr/ctxtRMMG1-MATNR")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void NormalizeElementId_ReducesToSessionIndependentForm(string raw, string expected)
    {
        Assert.Equal(expected, SapElementId.Normalize(raw));
    }

    [Fact]
    public void NormalizeElementId_TrimsSurroundingWhitespace()
    {
        Assert.Equal(
            "wnd[0]/usr/ctxtRMMG1-MATNR",
            SapElementId.Normalize("  /app/con[0]/ses[0]/wnd[0]/usr/ctxtRMMG1-MATNR  "));
    }
}

namespace RPA.Infrastructure.SAP.SapGuiElements;

using System.Runtime.Versioning;
using RPA.Domain.Interfaces;

/// <summary>
/// SAP ekranına sanal tuş (VKey) gönderir — F8 (Çalıştır), F3 (Geri), F4 (Arama yardımı),
/// F12 (İptal), Enter gibi.
///
/// <para>SAP'ta bu yol buton ID'siyle tıklamaktan daha sağlamdır: <c>sendVKey</c> pencere üzerinde
/// COM ile çalışır, ekran düzeni/araç çubuğu değişse de bozulmaz ve odaktan bağımsızdır.</para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SapGuiSendVKeyActivity : IActivity
{
    private readonly ISapGuiChannel _channel;

    public SapGuiSendVKeyActivity(ISapGuiChannel channel)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
    }

    public ActivityMetadata GetMetadata() => new()
    {
        ActivityId = "Sap.Gui.SendVKey",
        DisplayName = "SAP GUI Tuş Gönder",
        Category = "SAP",
        Description = "SAP ekranına sanal tuş gönderir (F8 Çalıştır, F3 Geri, F4 Arama yardımı, F12 İptal, Enter).",
        Inputs = new()
        {
            new ActivityParameter
            {
                Name = "key",
                Type = "string",
                Required = true,
                DefaultValue = "F8",
                Description = "Tuş: Enter, F1–F12, Shift+F1–F12, Ctrl+F1–F12, Ctrl+Shift+F1–F12 (veya 0–48 VKey numarası).",
            },
            new ActivityParameter
            {
                Name = "windowId",
                Type = "string",
                Required = false,
                DefaultValue = "wnd[0]",
                Description = "Hedef pencere. Ana ekran wnd[0]; açılan iletişim kutusu için wnd[1].",
            },
        },
        Outputs = new(),
        RequiredCapabilities = new() { "sap-gui" }
    };

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var key = context.GetVariable<string>("key");
        // Tanınmayan tuş adı BusinessException fırlatır (tasarım hatası, teknik arıza değil).
        var vKey = SapVirtualKey.Parse(key);

        var windowId = context.GetVariable<string>("windowId");
        windowId = string.IsNullOrWhiteSpace(windowId) ? "wnd[0]" : windowId.Trim();

        context.Log($"SAP GUI tuş gönderiliyor: {key} (VKey {vKey}) → {windowId}");
        await _channel.SendVKeyAsync(vKey, windowId);
        return new();
    }
}

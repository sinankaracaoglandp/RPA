namespace RPA.Domain.Interfaces;

/// <summary>
/// SAP GUI Scripting (fallback) kanalı — programatik arayüzü (NCo) olmayan ekranlar için
/// canlı SAP GUI oturumu üzerinden UI otomasyonu. Spec Bölüm 6, Bölüm 5.3.
///
/// Birincil kanal NCo (<see cref="ISapDataChannel"/>); GUI yalnızca fallback olarak kullanılır.
/// Studio'da her SAP aktivitesi hangi kanalın kullanılacağını açıkça seçer.
///
/// Kontrat Değişikliği 2026-07-05: <see cref="SelectTabAsync"/> eklendi (SAP tab-strip desteği).
/// </summary>
public interface ISapGuiChannel
{
    /// <summary>
    /// SAP GUI oturumuna bağlan (Connect/Login).
    /// </summary>
    /// <param name="systemId">System ID (örn. "DEV")</param>
    /// <param name="client">Client (örn. "100")</param>
    /// <param name="userId">User ID</param>
    /// <param name="password">Password (Vault'tan)</param>
    /// <param name="language">Dil (örn. "TR")</param>
    Task ConnectAsync(string systemId, string client, string userId, string password, string language = "EN");

    /// <summary>
    /// SAP ekranında transaction çalıştır.
    /// </summary>
    /// <param name="transactionCode">Transaksiyon kodu (örn. "MM01")</param>
    Task ExecuteTransactionAsync(string transactionCode);

    /// <summary>
    /// Element'e tıkla.
    /// </summary>
    /// <param name="elementId">Hiyerarşik ID (örn. "wnd[0]/usr/btnOK")</param>
    Task ClickAsync(string elementId);

    /// <summary>
    /// Alan'a metin yaz.
    /// </summary>
    /// <param name="elementId">Alan ID</param>
    /// <param name="text">Yazılacak metin</param>
    Task SetTextAsync(string elementId, string text);

    /// <summary>
    /// Alan'dan metni oku.
    /// </summary>
    /// <param name="elementId">Alan ID</param>
    /// <returns>Mevcut metin</returns>
    Task<string> GetTextAsync(string elementId);

    /// <summary>
    /// Tab-strip sekmesi seç.
    /// </summary>
    /// <param name="elementId">Tab element ID (örn. "wnd[0]/usr/tabsTABSTRIP/tabpTAB01")</param>
    Task SelectTabAsync(string elementId);

    /// <summary>
    /// Menü çubuğunda metin yoluyla gezinerek bir menü öğesi seçer (element ID gerekmez).
    /// </summary>
    /// <param name="menuPath">"/" ile ayrılmış menü metni yolu (örn. "Sistem/Liste/Yazdır")</param>
    Task SelectMenuAsync(string menuPath);

    /// <summary>
    /// Ekrana sanal tuş (VKey) gönderir — F8 (Çalıştır), F3 (Geri), F4 (Arama yardımı),
    /// F12 (İptal), Enter gibi. SAP'ta buton ID'siyle tıklamaktan daha sağlamdır: ekran
    /// düzeni değişse de tuş çalışır ve odaktan bağımsızdır.
    /// </summary>
    /// <param name="vKey">SAP VKey numarası (0=Enter, 1–12=F1–F12, 13–24=Shift+F1–F12,
    /// 25–36=Ctrl+F1–F12, 37–48=Ctrl+Shift+F1–F12)</param>
    /// <param name="windowId">Tuşun gönderileceği pencere (varsayılan ana ekran; popup için "wnd[1]")</param>
    Task SendVKeyAsync(int vKey, string windowId = "wnd[0]");

    /// <summary>
    /// ALV grid oku (SAP listeyi otomatik al).
    /// </summary>
    /// <param name="gridId">Grid element ID</param>
    /// <returns>Satır/sütun veri</returns>
    Task<List<Dictionary<string, object?>>> ReadGridAsync(string gridId);

    /// <summary>
    /// Ekran görüntüsü al (debug/error analysis için).
    /// </summary>
    /// <returns>PNG bytes</returns>
    Task<byte[]> CaptureScreenAsync();

    /// <summary>
    /// Oturumdan çık (Disconnect/Logout).
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// Oturum sağlık kontrolü.
    /// </summary>
    Task<bool> IsHealthyAsync();
}

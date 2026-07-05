namespace RPA.Infrastructure.SAP;

/// <summary>
/// Tek bir SAP GUI Scripting COM oturumunun (GuiSession) düşük seviye soyutlaması.
/// Gerçek implementasyon <c>sapfewse.ocx</c> COM nesnelerini sarmalar; birim testlerde
/// mock'lanır. Bu soyutlama sayesinde <see cref="SapGuiChannel"/> COM'a doğrudan bağlı değildir.
/// </summary>
public interface ISapGuiSession
{
    /// <summary>Oturum kimliği (Connection/Session index veya COM handle).</summary>
    string Id { get; }

    /// <summary>Oturum bağlı ve kullanılabilir mi?</summary>
    bool IsConnected { get; }

    /// <summary>Transaction başlat (örn. "/nMM01").</summary>
    Task StartTransactionAsync(string transactionCode);

    /// <summary>Element'e metin yaz.</summary>
    Task SetTextAsync(string elementId, string text);

    /// <summary>Element'ten metin oku.</summary>
    Task<string> GetTextAsync(string elementId);

    /// <summary>Element'e tıkla / bas.</summary>
    Task ClickAsync(string elementId);

    /// <summary>Tab-strip sekmesi seç.</summary>
    Task SelectTabAsync(string elementId);

    /// <summary>ALV grid içeriğini oku.</summary>
    Task<List<Dictionary<string, object?>>> ReadGridAsync(string gridId);

    /// <summary>Aktif pencerenin ekran görüntüsünü al (PNG).</summary>
    Task<byte[]> CaptureScreenAsync();

    /// <summary>Oturumu kapat.</summary>
    void Close();
}

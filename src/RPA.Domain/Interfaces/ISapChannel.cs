namespace RPA.Domain.Interfaces;

/// <summary>
/// SAP entegrasyonu için hibrit kanal — veri (NCo) ve GUI.
/// Spec Bölüm 6: Programatik (NCo) birincil, GUI fallback.
/// Studio'da her SAP aktivitesi hangi kanalın kullanılacağını açıkça seçer.
/// </summary>
public interface ISapDataChannel
{
    /// <summary>
    /// BAPI/RFC çağrı — doğrudan SAP bağlantısı üzerinden, veri alışverişi.
    /// </summary>
    /// <param name="bapiName">BAPI adı (örn. "BAPI_MATERIAL_CREATE")</param>
    /// <param name="inputs">Input parametreleri (ad-değer)</param>
    /// <param name="tableInputs">Tablo input parametreleri</param>
    /// <returns>Output parametreleri + tablo sonuçları</returns>
    /// <exception cref="BusinessException">SAP hata türü E/A (iş kuralı)</exception>
    /// <exception cref="SystemException">Bağlantı hata, RFC_COMMUNICATION_FAILURE (teknik)</exception>
    Task<SapCallResult> CallBapiAsync(
        string bapiName,
        Dictionary<string, object?> inputs,
        Dictionary<string, List<Dictionary<string, object?>>>? tableInputs = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// RFC çağrı — BAPI'dan daha düşük seviye, custom function module çağırır.
    /// </summary>
    Task<SapCallResult> CallRfcAsync(
        string rfcName,
        Dictionary<string, object?> inputs,
        Dictionary<string, List<Dictionary<string, object?>>>? tableInputs = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tablo oku (RFC_READ_TABLE — standard SAP RFC).
    /// </summary>
    /// <param name="tableName">SAP tablo adı (örn. "MARA")</param>
    /// <param name="fields">Hangi alanlar (null = tümü)</param>
    /// <param name="where">WHERE koşulu (örn. "MATNR = '12345'")</param>
    /// <returns>Satır listesi</returns>
    Task<List<Dictionary<string, object?>>> ReadTableAsync(
        string tableName,
        List<string>? fields = null,
        string? where = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// BAPI_TRANSACTION_COMMIT — SAP transaksiyonu commit et.
    /// </summary>
    Task<SapCallResult> CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rollback (BAPI_TRANSACTION_ROLLBACK).
    /// </summary>
    Task<SapCallResult> RollbackAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Bağlantı sağlık kontrolü.
    /// </summary>
    Task<bool> IsHealthyAsync();
}

public interface ISapGuiChannel
{
    /// <summary>
    /// SAP GUI oturumuna bağlan.
    /// </summary>
    /// <param name="systemId">System ID (örn. "DEV")</param>
    /// <param name="client">Client (örn. "100")</param>
    /// <param name="userId">User ID</param>
    /// <param name="password">Password (Vault'tan)</param>
    /// <param name="language">Dil (örn. "TR")</param>
    Task LoginAsync(string systemId, string client, string userId, string password, string language = "EN");

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
    /// Oturumdan çık.
    /// </summary>
    Task LogoutAsync();

    /// <summary>
    /// Oturum sağlık kontrolü.
    /// </summary>
    Task<bool> IsHealthyAsync();
}

public class SapCallResult
{
    /// <summary>
    /// Başarılı mı?
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Return Type (BAPI): E (Error), W (Warning), I (Info), S (Success)
    /// </summary>
    public char? ReturnType { get; set; }

    /// <summary>
    /// Return Message
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Output parametreleri
    /// </summary>
    public Dictionary<string, object?> Outputs { get; set; } = new();

    /// <summary>
    /// Tablo çıkışları
    /// </summary>
    public Dictionary<string, List<Dictionary<string, object?>>> TableOutputs { get; set; } = new();
}

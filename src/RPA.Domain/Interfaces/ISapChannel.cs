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

// NOT: ISapGuiChannel arayüzü kendi dosyasına taşındı: Interfaces/ISapGuiChannel.cs
// (Kontrat Değişikliği 2026-07-05 — SelectTabAsync eklendi; bkz. CLAUDE.md)

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

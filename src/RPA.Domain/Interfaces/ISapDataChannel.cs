namespace RPA.Domain.Interfaces;

/// <summary>
/// SAP programatik veri kanalı — SAP .NET Connector (NCo 3.1) üzerinden BAPI/RFC çağrıları (Task 4.2).
/// Spec Bölüm 6: NCo birincil (programatik, yüksek hacim) kanaldır; <see cref="ISapGuiChannel"/> yalnızca fallback.
///
/// Bağlantı yaşam döngüsü platform tarafından yönetilir (bkz. SapConnectionPool) — aktivite başına
/// bağlantı açılmaz. Kimlik bilgileri (sistem/client/kullanıcı/dil) Credential Vault'tan çözülür,
/// asla plaintext saklanmaz (CLAUDE.md Kısım 3).
///
/// Kontrat Değişikliği 2026-07-05: Arayüz <c>ISapChannel.cs</c>'ten kendi dosyasına taşındı
/// (<see cref="ISapGuiChannel"/> ile aynı desen). İmzalar değişmedi.
/// </summary>
public interface ISapDataChannel
{
    /// <summary>
    /// BAPI/RFC çağrı — doğrudan SAP bağlantısı üzerinden, veri alışverişi.
    /// </summary>
    /// <param name="bapiName">BAPI adı (örn. "BAPI_MATERIAL_SAVEDATA")</param>
    /// <param name="inputs">Import parametreleri (ad-değer)</param>
    /// <param name="tableInputs">Tablo import parametreleri</param>
    /// <returns>Output parametreleri + tablo sonuçları</returns>
    /// <exception cref="RPA.Domain.Exceptions.BusinessException">SAP dönüş türü E/A (iş kuralı)</exception>
    /// <exception cref="RPA.Domain.Exceptions.SystemException">RFC_COMMUNICATION_FAILURE / timeout (teknik)</exception>
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
    /// BAPI_TRANSACTION_COMMIT — SAP transaksiyonu commit et (spec 5.2 — explicit, auto-commit değil).
    /// </summary>
    Task<SapCallResult> CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rollback (BAPI_TRANSACTION_ROLLBACK).
    /// </summary>
    Task<SapCallResult> RollbackAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Bağlantı sağlık kontrolü (havuzdan bağlantı ödünç alınabiliyor mu?).
    /// </summary>
    Task<bool> IsHealthyAsync();
}

/// <summary>
/// SAP BAPI/RFC çağrı sonucu — output parametreleri, tablo çıktıları ve SAP dönüş mesajı.
/// </summary>
public class SapCallResult
{
    /// <summary>Başarılı mı? (SAP dönüş türü E/A değilse true).</summary>
    public bool Success { get; set; }

    /// <summary>Return Type (BAPI): E (Error), A (Abort), W (Warning), I (Info), S (Success).</summary>
    public char? ReturnType { get; set; }

    /// <summary>Return Message.</summary>
    public string? Message { get; set; }

    /// <summary>Output (export) parametreleri.</summary>
    public Dictionary<string, object?> Outputs { get; set; } = new();

    /// <summary>Tablo çıkışları.</summary>
    public Dictionary<string, List<Dictionary<string, object?>>> TableOutputs { get; set; } = new();
}

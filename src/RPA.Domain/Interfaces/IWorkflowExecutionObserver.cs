namespace RPA.Domain.Interfaces;

/// <summary>
/// Workflow yürütme sırasında node yaşam döngüsü olaylarını dinleyen gözlemci (opsiyonel).
/// BaseRunner her node başlangıcında ve (aktivite/component) tamamlanışında çağırır.
/// Canlı konsol/izleme (Studio footer) için kullanılır.
///
/// <para><b>Güvenlik:</b> Değerler kaynakta (BaseRunner) maskelenip kısaltılarak
/// düz string olarak taşınır — Credential tipli girişler asla açık gönderilmez
/// (bkz. CLAUDE.md Kısım 3). Implementasyonlar <b>asla istisna fırlatmamalı</b>;
/// gözlemci hatası yürütmeyi bozmaz (BaseRunner çağrıları try/catch ile sarar).</para>
/// </summary>
public interface IWorkflowExecutionObserver
{
    /// <summary>Bir node çalışmaya başladığında.</summary>
    void OnNodeStarted(NodeExecutionEvent evt);

    /// <summary>Bir node tamamlandığında (başarı) veya hata ile bittiğinde.</summary>
    void OnNodeCompleted(NodeExecutionEvent evt);
}

/// <summary>
/// Node yürütme olayı. Değerler görüntülenebilir (maskeli/kısaltılmış) string'lerdir.
/// </summary>
public sealed record NodeExecutionEvent
{
    public required Guid JobRunId { get; init; }
    public required string NodeId { get; init; }
    public required string NodeType { get; init; }

    /// <summary>Aktivite node'ları için aktivite ID'si (örn. <c>Desktop.GetText</c>); diğerlerinde null.</summary>
    public string? ActivityId { get; init; }

    /// <summary>Maskeli/kısaltılmış giriş değerleri (yalnız aktivite/component tamamlanışında dolu).</summary>
    public IReadOnlyDictionary<string, string?>? Inputs { get; init; }

    /// <summary>Maskeli/kısaltılmış çıkış değerleri (yalnız tamamlanışta dolu).</summary>
    public IReadOnlyDictionary<string, string?>? Outputs { get; init; }

    /// <summary>
    /// Node tamamlandıktan SONRA görünür durumdaki tüm workflow değişkenlerinin maskeli/kısaltılmış
    /// anlık görüntüsü (iç scope dıştakini gölgeler). Tasarım/test sırasında ekrandan okunan
    /// değerlerin (ör. <c>list&lt;object&gt;</c> grid satırları) konsolda izlenmesi içindir.
    /// Yalnız tamamlanma olaylarında dolu; başlangıç olaylarında null.
    /// </summary>
    public IReadOnlyDictionary<string, string?>? Variables { get; init; }

    /// <summary>Hata mesajı (varsa). Null ise başarı.</summary>
    public string? Error { get; init; }

    /// <summary>Hata iş kuralı istisnası mı (true) yoksa sistem hatası mı (false).</summary>
    public bool IsBusinessError { get; init; }
}

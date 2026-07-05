namespace RPA.Infrastructure.SAP.Nco;

using System.Runtime.Versioning;

/// <summary>
/// SAP NCo 3.1 (SAP.Middleware.Connector) soyutlama katmanı (Task 4.2).
///
/// NCo 3.1 kütüphanesi tescillidir (NuGet'te dağıtılmaz; SAP Marketplace'ten indirilir) ve
/// yalnızca canlı bir SAP sistemine bağlanır. Bu yüzden fiziksel RFC bağlantısı bir arayüz
/// (<see cref="IRfcConnection"/>) ardına soyutlanır — birim testlerde mock'lanabilir, gerçek
/// implementasyon (RfcDestination/RfcRepository sarmalayan) DEP entegrasyon testinde kullanılır.
/// </summary>
[SupportedOSPlatform("windows")]
public interface IRfcConnection : IDisposable
{
    /// <summary>Fiziksel bağlantı kimliği (havuz izleme/loglama için).</summary>
    string Id { get; }

    /// <summary>Bağlantı hâlâ canlı/kullanılabilir mi? (ping/state).</summary>
    bool IsAlive { get; }

    /// <summary>
    /// Bir RFC/BAPI function module'ünü çalıştır.
    /// </summary>
    /// <param name="functionName">Function module adı (örn. "BAPI_TRANSACTION_COMMIT")</param>
    /// <param name="importing">Import parametreleri</param>
    /// <param name="tables">Tablo parametreleri (giriş; SAP tarafında güncellenir ve çıktıda döner)</param>
    /// <exception cref="RfcCommunicationException">RFC_COMMUNICATION_FAILURE / SYSTEM_FAILURE (teknik)</exception>
    Task<RfcInvokeResult> InvokeAsync(
        string functionName,
        IReadOnlyDictionary<string, object?> importing,
        IReadOnlyDictionary<string, List<Dictionary<string, object?>>> tables,
        CancellationToken cancellationToken);
}

/// <summary>
/// Fiziksel RFC bağlantısı açan fabrika. Gerçek implementasyon NCo
/// <c>RfcDestinationManager.GetDestination</c> sarmalar; testlerde mock'lanır.
/// </summary>
[SupportedOSPlatform("windows")]
public interface IRfcConnectionFactory
{
    /// <summary>Verilen ayarlarla yeni bir fiziksel RFC bağlantısı aç.</summary>
    /// <exception cref="RfcCommunicationException">Bağlantı kurulamadı (teknik)</exception>
    Task<IRfcConnection> OpenAsync(SapConnectionSettings settings, CancellationToken cancellationToken);
}

/// <summary>
/// Bir RFC/BAPI çağrısının ham sonucu — export parametreleri ve tablo çıktıları.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RfcInvokeResult
{
    /// <summary>Export (çıkış) parametreleri. RETURN yapısı burada veya <see cref="Tables"/> içinde olabilir.</summary>
    public Dictionary<string, object?> Exporting { get; init; } = new();

    /// <summary>Tablo çıktıları (RETURN tablosu, DATA, FIELDS vb.).</summary>
    public Dictionary<string, List<Dictionary<string, object?>>> Tables { get; init; } = new();
}

/// <summary>
/// RFC iletişim hatası (RFC_COMMUNICATION_FAILURE / SYSTEM_FAILURE) — teknik, retry edilebilir.
/// Havuz ölü bağlantıyı atar ve yeniden dener; kanal <see cref="RPA.Domain.Exceptions.SystemException"/>'a çevirir.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RfcCommunicationException : Exception
{
    public RfcCommunicationException(string message)
        : base(message)
    {
    }

    public RfcCommunicationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Bir SAP sistemine bağlantı için çözülmüş ayarlar. Şifre Vault'tan çözülür ve yalnızca bu
/// nesnenin ömrü boyunca bellekte tutulur (kalıcı saklama yok — CLAUDE.md Kısım 3).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SapConnectionSettings
{
    public required string SystemId { get; init; }
    public required string Client { get; init; }
    public required string User { get; init; }
    public required string Password { get; init; }
    public string Language { get; init; } = "EN";
    public string? AppServerHost { get; init; }
    public string? SystemNumber { get; init; }

    /// <summary>Bağlantı/işlem zaman aşımı (saniye).</summary>
    public int TimeoutSeconds { get; init; } = 60;
}

/// <summary>
/// <see cref="SapConnectionPool"/> davranış ayarları.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SapNcoPoolOptions
{
    /// <summary>Havuzdaki maksimum eşzamanlı bağlantı sayısı.</summary>
    public int MaxPoolSize { get; init; } = 5;

    /// <summary>Havuz doluyken bağlantı beklemenin zaman aşımı (saniye).</summary>
    public int BorrowTimeoutSeconds { get; init; } = 30;

    /// <summary>COMMUNICATION_FAILURE durumunda yeniden deneme sayısı.</summary>
    public int MaxRetries { get; init; } = 2;
}

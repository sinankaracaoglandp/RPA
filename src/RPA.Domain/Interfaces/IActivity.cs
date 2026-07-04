namespace RPA.Domain.Interfaces;

using RPA.Domain.Enums;

/// <summary>
/// Her aktivite (sap, web, api, excel, email, etc.) bu arayüzü implements eder.
/// BaseRunner, IActivity node'ları çalıştırırken bu arayüzü kullanır.
/// </summary>
public interface IActivity
{
    /// <summary>
    /// Aktiviteyi çalıştır.
    /// </summary>
    /// <param name="context">Çalışma ortamı (değişkenler, credential vault, log)</param>
    /// <returns>Çıkış değerleri (JSON)</returns>
    /// <exception cref="BusinessException">İş kuralı hlal (expected)</exception>
    /// <exception cref="SystemException">Teknik hata (retry edilebilir)</exception>
    Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context);

    /// <summary>
    /// Aktivitenin metadata'sı (ad, açıklama, input/output şeması).
    /// Katalogdan Studio tarafından okunur.
    /// </summary>
    ActivityMetadata GetMetadata();
}

/// <summary>
/// Aktivite çalıştırma ortamı — değişkenlere erişim, credential'lar, loglama.
/// BaseRunner tarafından sağlanır.
/// </summary>
public interface IActivityExecutionContext
{
    /// <summary>
    /// Değişken scope'undan değer al.
    /// </summary>
    T GetVariable<T>(string name);

    /// <summary>
    /// Değişkeni set et.
    /// </summary>
    void SetVariable(string name, object? value);

    /// <summary>
    /// Credential vault'tan secret al (encrypted, SecureString).
    /// </summary>
    Task<string> GetCredentialAsync(string credentialName);

    /// <summary>
    /// Asset (ortam konfigürasyonu) al.
    /// </summary>
    Task<string?> GetAssetAsync(string assetName);

    /// <summary>
    /// Yapılandırılmış log yaz (korelasyon ID otomatik).
    /// </summary>
    void Log(string message, LogLevel level = LogLevel.Information);

    /// <summary>
    /// Zamanşı (SA vs UTC).
    /// </summary>
    string TimeZone { get; }

    /// <summary>
    /// İlgili JobRun ID (log tagging için).
    /// </summary>
    Guid JobRunId { get; }
}

/// <summary>
/// Aktivite metadatası — Katalogda saklanır, Studio tarafından toolbox'a doldurulur.
/// </summary>
public class ActivityMetadata
{
    /// <summary>
    /// Benzersiz kod (örn. "Sap.Gui.Click", "Web.Fill", "Api.Post")
    /// </summary>
    public required string ActivityId { get; set; }

    /// <summary>
    /// Ekran etiketi (örn. "SAP Tıkla", "Web Alan Doldur")
    /// </summary>
    public required string DisplayName { get; set; }

    /// <summary>
    /// Kategori (örn. "SAP", "Web", "Excel", "Email")
    /// </summary>
    public string Category { get; set; } = "General";

    /// <summary>
    /// Kısa açıklama
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Giriş parametreleri
    /// </summary>
    public List<ActivityParameter> Inputs { get; set; } = new();

    /// <summary>
    /// Çıkış parametreleri
    /// </summary>
    public List<ActivityParameter> Outputs { get; set; } = new();

    /// <summary>
    /// İlk kez bu aktivite çalıştırıldığında default property değerleri (JSON).
    /// Studio'da "yeni node ekle" sırasında önceden doldurur.
    /// </summary>
    public Dictionary<string, object?> DefaultProperties { get; set; } = new();

    /// <summary>
    /// Etiketi (örn. "sap-gui", "sap-nco", "web", "async") — robot eşleşmesi için.
    /// </summary>
    public List<string> RequiredCapabilities { get; set; } = new();

    /// <summary>
    /// Exception sınıflandırma kuralları (SAP dönüş mesaj tipi, HTTP status, vb.)
    /// </summary>
    public ExceptionClassificationRule? ExceptionClassification { get; set; }
}

public class ActivityParameter
{
    public required string Name { get; set; }
    public required string Type { get; set; } // string, int, bool, DateTime, DataTable, JSON, Credential
    public bool Required { get; set; } = true;
    public object? DefaultValue { get; set; }
    public string? Description { get; set; }
}

public class ExceptionClassificationRule
{
    /// <summary>
    /// Örn. SAP: "ReturnType == 'E'" → Business
    ///      HTTP: "StatusCode >= 500" → System
    /// </summary>
    public string Condition { get; set; } = "";
    public ExceptionType Classification { get; set; }
}

public enum LogLevel { Debug, Information, Warning, Error, Critical }

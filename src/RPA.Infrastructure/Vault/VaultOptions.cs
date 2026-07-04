namespace RPA.Infrastructure.Vault;

/// <summary>
/// appsettings.json "Vault" bölümü kök nesnesi.
/// Spec Bölüm 5.5, 10: credential vault yapılandırması.
/// </summary>
public class VaultOptions
{
    public const string SectionName = "Vault";

    /// <summary>Aktif backend: "HashiCorp" veya "Dpapi".</summary>
    public string Type { get; set; } = "Dpapi";

    public HashiCorpOptions HashiCorp { get; set; } = new();
    public DpapiOptions Dpapi { get; set; } = new();
}

/// <summary>
/// HashiCorp Vault (KV v2) bağlantı ayarları.
/// </summary>
public class HashiCorpOptions
{
    /// <summary>ör. https://vault.example.com:8200</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Vault token (X-Vault-Token). Üretimde AppRole ile alınabilir.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>KV v2 mount yolu (varsayılan "secret").</summary>
    public string Mount { get; set; } = "secret";

    /// <summary>Ağ hatalarında yeniden deneme sayısı.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Yeniden denemeler arası baz gecikme (ms) — exponential backoff.</summary>
    public int RetryBaseDelayMs { get; set; } = 200;
}

/// <summary>
/// DPAPI-şifreli yerel vault (fallback/test) ayarları.
/// </summary>
public class DpapiOptions
{
    /// <summary>
    /// Şifreli secret dosyalarının saklandığı dizin.
    /// Boşsa %ProgramData%\RPA\vault kullanılır.
    /// </summary>
    public string? StorePath { get; set; }
}

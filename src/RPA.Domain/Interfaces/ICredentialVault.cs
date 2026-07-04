namespace RPA.Domain.Interfaces;

/// <summary>
/// Credential yönetimi — SAP, Web, API, E-posta, TOTP secret'ları.
/// Spec Bölüm 5.5: credential asla plaintext DB'de saklanmaz, Vault referansı tutulur.
/// İki implementasyon: HashiCorp Vault (bulut/on-prem), DPAPI (lokal Windows).
/// </summary>
public interface ICredentialVault
{
    /// <summary>
    /// Secret'ı vault'tan al.
    /// </summary>
    /// <param name="key">Vault'ta saklanan key (örn. "sap-dev-user-password")</param>
    /// <returns>SecureString — bellekte kriptolu, plaintext açılmaz loglarda</returns>
    Task<SecureString> GetSecretAsync(string key);

    /// <summary>
    /// Secret'ı vault'a koy.
    /// </summary>
    /// <param name="key">Vault key</param>
    /// <param name="secret">Değer</param>
    /// <param name="metadata">Etiketler (SAP, Web, API, Email, TOTP) — bulunabilirlik için</param>
    Task StoreSecretAsync(string key, SecureString secret, Dictionary<string, string>? metadata = null);

    /// <summary>
    /// Secret'ı vault'tan sil.
    /// </summary>
    Task DeleteSecretAsync(string key);

    /// <summary>
    /// Vault'ta key var mı?
    /// </summary>
    Task<bool> ExistsAsync(string key);

    /// <summary>
    /// Etiketle filtreleme (örn. type=SAP).
    /// </summary>
    Task<IEnumerable<string>> ListSecretsByTagAsync(string tag);
}

/// <summary>
/// Windows DPAPI kullanarak şifreli SecureString.
/// Vault erişimi offline veya test sırasında için.
/// </summary>
public class SecureString
{
    private byte[] _encryptedBytes;

    public SecureString(string plaintext)
    {
        _encryptedBytes = EncryptString(plaintext);
    }

    /// <summary>
    /// Decrypt et — sadece gerektiğinde (aktivite çalıştırma zamanı).
    /// </summary>
    public string Decrypt()
    {
        return DecryptString(_encryptedBytes);
    }

    private static byte[] EncryptString(string plaintext)
    {
        // DPAPI tarafından encrypt edilecek — implement edge'de
        throw new NotImplementedException();
    }

    private static string DecryptString(byte[] encryptedBytes)
    {
        // DPAPI tarafından decrypt edilecek
        throw new NotImplementedException();
    }

    public override string ToString() => "[SecureString]";
}

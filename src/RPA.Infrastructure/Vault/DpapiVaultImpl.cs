namespace RPA.Infrastructure.Vault;

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using RPA.Domain.Interfaces;

/// <summary>
/// DPAPI-şifreli yerel vault implementasyonu (fallback / test / offline).
/// Spec Bölüm 5.5, 10.
///
/// Her secret, diske DPAPI (CurrentUser) ile şifrelenmiş olarak yazılır.
/// Plaintext hiçbir zaman diske veya loglara yazılmaz — yalnızca
/// <see cref="SecureString"/> içinde DPAPI şifreli byte olarak taşınır.
/// Dosya adı, key'in SHA-256 hex özetidir (dosya sistemi güvenli);
/// orijinal key ve metadata şifreli dosyanın içinde tutulur.
/// </summary>
public sealed class DpapiVaultImpl : ICredentialVault
{
    private readonly string _storePath;
    private readonly ILogger<DpapiVaultImpl> _logger;

    public DpapiVaultImpl(IOptions<VaultOptions> options, ILogger<DpapiVaultImpl> logger)
    {
        _logger = logger;
        var configured = options.Value.Dpapi.StorePath;
        _storePath = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "RPA", "vault")
            : configured;
        Directory.CreateDirectory(_storePath);
    }

    public Task<SecureString> GetSecretAsync(string key)
    {
        var path = PathForKey(key);
        if (!File.Exists(path))
        {
            throw new KeyNotFoundException($"Vault key bulunamadı: '{key}'.");
        }

        var record = ReadRecord(path);
        _logger.LogInformation("Vault secret alındı: {Key}", key);
        // Değer daima DPAPI şifreli byte olarak taşınır — plaintext açılmaz.
        return Task.FromResult(SecureString.FromEncryptedBytes(
            Convert.FromBase64String(record.EncryptedValueBase64)));
    }

    public Task StoreSecretAsync(
        string key, SecureString secret, Dictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(secret);

        var record = new SecretRecord
        {
            Key = key,
            EncryptedValueBase64 = Convert.ToBase64String(secret.GetEncryptedBytes()),
            Metadata = metadata ?? new Dictionary<string, string>(),
        };

        WriteRecord(PathForKey(key), record);
        // Yalnızca key ve metadata anahtarları loglanır — değer asla.
        _logger.LogInformation("Vault secret saklandı: {Key}, tags: {Tags}",
            key, string.Join(",", record.Metadata.Keys));
        return Task.CompletedTask;
    }

    public Task DeleteSecretAsync(string key)
    {
        var path = PathForKey(key);
        if (File.Exists(path))
        {
            File.Delete(path);
            _logger.LogInformation("Vault secret silindi: {Key}", key);
        }
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key)
        => Task.FromResult(File.Exists(PathForKey(key)));

    public Task<IEnumerable<string>> ListSecretsByTagAsync(string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        var matches = new List<string>();
        foreach (var file in Directory.EnumerateFiles(_storePath, "*.secret"))
        {
            SecretRecord record;
            try
            {
                record = ReadRecord(file);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Vault kaydı okunamadı, atlanıyor: {File}", file);
                continue;
            }

            if (MatchesTag(record.Metadata, tag))
            {
                matches.Add(record.Key);
            }
        }

        return Task.FromResult<IEnumerable<string>>(matches);
    }

    private static bool MatchesTag(Dictionary<string, string> metadata, string tag)
    {
        // "key=value" veya sadece "value"/"key" ile eşleşme.
        var eq = tag.IndexOf('=');
        if (eq >= 0)
        {
            var k = tag[..eq];
            var v = tag[(eq + 1)..];
            return metadata.TryGetValue(k, out var actual)
                && string.Equals(actual, v, StringComparison.OrdinalIgnoreCase);
        }

        return metadata.Keys.Any(k => string.Equals(k, tag, StringComparison.OrdinalIgnoreCase))
            || metadata.Values.Any(v => string.Equals(v, tag, StringComparison.OrdinalIgnoreCase));
    }

    private string PathForKey(string key)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        var name = Convert.ToHexString(bytes).ToLowerInvariant();
        return Path.Combine(_storePath, name + ".secret");
    }

    private static SecretRecord ReadRecord(string path)
    {
        var json = File.ReadAllText(path);
        return JsonConvert.DeserializeObject<SecretRecord>(json)
            ?? throw new InvalidDataException($"Vault kaydı bozuk: {path}");
    }

    private static void WriteRecord(string path, SecretRecord record)
    {
        var json = JsonConvert.SerializeObject(record, Formatting.Indented);
        // Atomik yazım: temp'e yaz, sonra taşı.
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }

    private sealed class SecretRecord
    {
        public string Key { get; set; } = string.Empty;

        /// <summary>DPAPI ile şifrelenmiş değerin base64'ü — asla plaintext.</summary>
        public string EncryptedValueBase64 { get; set; } = string.Empty;

        public Dictionary<string, string> Metadata { get; set; } = new();
    }
}

namespace RPA.Infrastructure.OTP;

using System.Security.Cryptography;
using System.Text;
using RPA.Domain.Interfaces;

/// <summary>
/// <see cref="IOtpCodeProtector"/> için AES-256 (GCM benzeri değil; CBC + rastgele IV) implementasyonu (Spec Bölüm 7).
/// Anahtar DPAPI/Vault'tan gelir; plaintext OTP kodu DB'ye asla yazılmaz, loglarda "[MASKED]" gösterilir.
/// </summary>
public sealed class AesOtpCodeProtector : IOtpCodeProtector
{
    private readonly byte[] _key;

    /// <param name="key">32 byte (256-bit) AES anahtarı (Vault/DPAPI'den).</param>
    public AesOtpCodeProtector(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != 32)
        {
            throw new ArgumentException("AES anahtarı 32 byte (256-bit) olmalıdır.", nameof(key));
        }

        _key = key;
    }

    public string Encrypt(string plaintextCode)
    {
        ArgumentNullException.ThrowIfNull(plaintextCode);

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plaintextCode);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        // IV'yi cipher metnin önüne ekle: [IV(16)][cipher]
        var combined = new byte[aes.IV.Length + cipherBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, combined, 0, aes.IV.Length);
        Buffer.BlockCopy(cipherBytes, 0, combined, aes.IV.Length, cipherBytes.Length);
        return Convert.ToBase64String(combined);
    }

    public string Decrypt(string cipherText)
    {
        ArgumentNullException.ThrowIfNull(cipherText);

        var combined = Convert.FromBase64String(cipherText);
        using var aes = Aes.Create();
        aes.Key = _key;

        var iv = new byte[16];
        if (combined.Length < iv.Length)
        {
            throw new CryptographicException("Şifreli metin geçersiz (IV eksik).");
        }

        Buffer.BlockCopy(combined, 0, iv, 0, iv.Length);
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        var cipherBytes = decryptor.TransformFinalBlock(combined, iv.Length, combined.Length - iv.Length);
        return Encoding.UTF8.GetString(cipherBytes);
    }

    public string Mask(string code) => "[MASKED]";
}

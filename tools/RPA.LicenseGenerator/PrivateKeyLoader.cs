using System.Security.Cryptography;

namespace RPA.LicenseGenerator;

/// <summary>
/// Uretici (vendor) ozel anahtarini sifreli PEM/PKCS#8 dosyasindan yukler. Parola yalnizca
/// adi argumanla verilen ORTAM DEGISKENINDEN okunur — komut satirinda parola tasinmaz
/// (process listesi/shell gecmisi sizintisi). Hicbir hata yolunda parola, anahtar icerigi
/// veya alttaki kripto istisnasi mesaji disari verilmez.
/// </summary>
public static class PrivateKeyLoader
{
    public static RSA Load(string keyPath, string passwordEnvironmentVariable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordEnvironmentVariable);

        if (!File.Exists(keyPath))
            throw new LicenseGenerationException($"Ozel anahtar dosyasi bulunamadi: {keyPath}");

        var password = Environment.GetEnvironmentVariable(passwordEnvironmentVariable);
        if (string.IsNullOrEmpty(password))
            throw new LicenseGenerationException(
                $"'{passwordEnvironmentVariable}' ortam degiskeni tanimli degil veya bos.");

        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromEncryptedPem(File.ReadAllText(keyPath), password);
            return rsa;
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException or IOException)
        {
            rsa.Dispose();

            // Inner exception BILEREK zincirlenmez: kripto/IO istisna metinleri anahtar
            // materyalini veya parolayi yansitabilir.
            throw new LicenseGenerationException(
                "Ozel anahtar yuklenemedi. Anahtar dosyasinin sifreli PKCS#8 oldugunu ve " +
                $"'{passwordEnvironmentVariable}' degiskenindeki parolanin dogru oldugunu dogrulayin.");
        }
    }
}

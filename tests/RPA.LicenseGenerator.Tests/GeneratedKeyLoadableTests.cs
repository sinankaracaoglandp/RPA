namespace RPA.LicenseGenerator.Tests;

using System.Security.Cryptography;
using RPA.LicenseGenerator;

/// <summary>
/// LicenseStudio'nun ekrandan urettigi vendor anahtarinin, imzalama yolunun (PrivateKeyLoader)
/// yukleyebildigi bicimde olmasini garanti eder. KeyGenForm net10.0-windows oldugundan buradan
/// referans edilemez; bu yuzden URETIM PARAMETRELERI (RSA-3072, AES-256-CBC, SHA-256, 600k iter,
/// "ENCRYPTED PRIVATE KEY" PEM) birebir tekrarlanip round-trip dogrulanir. Parametreler kayarsa
/// bu test kirilir.
/// </summary>
public sealed class GeneratedKeyLoadableTests
{
    [Fact]
    public void KeyGeneratedWithStudioParameters_LoadsAndSigns()
    {
        const string password = "test-parola-123";
        const string envVar = "RPA_LICENSE_STUDIO_TEST_PW";
        var keyPath = Path.Combine(Path.GetTempPath(), "rpa-keygen-" + Guid.NewGuid().ToString("N") + ".pem");

        // --- KeyGenForm.OnGenerate ile AYNI uretim ---
        using (var rsa = RSA.Create(3072))
        {
            var pbe = new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 600_000);
            var privatePem = new string(PemEncoding.Write("ENCRYPTED PRIVATE KEY",
                rsa.ExportEncryptedPkcs8PrivateKey(password.AsSpan(), pbe)));
            File.WriteAllText(keyPath, privatePem);
        }

        try
        {
            Environment.SetEnvironmentVariable(envVar, password);

            // Imzalama yolunun kullandigi gercek yukleyici bu anahtari acabilmeli.
            using var loaded = PrivateKeyLoader.Load(keyPath, envVar);

            var data = new byte[] { 1, 2, 3, 4, 5 };
            var signature = loaded.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
            Assert.True(loaded.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss));
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVar, null);
            if (File.Exists(keyPath)) File.Delete(keyPath);
        }
    }
}

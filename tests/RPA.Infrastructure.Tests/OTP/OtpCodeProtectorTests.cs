namespace RPA.Infrastructure.Tests.OTP;

using System.Security.Cryptography;
using RPA.Infrastructure.OTP;

/// <summary>
/// <see cref="AesOtpCodeProtector"/> testleri (Task 4.3, Spec Bölüm 7).
/// AES-256 şifreleme round-trip, log maskeleme ve anahtar doğrulaması.
/// </summary>
public class OtpCodeProtectorTests
{
    private static byte[] NewKey()
    {
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        return key;
    }

    [Fact]
    public void EncryptDecrypt_RoundTrip_ReturnsOriginal()
    {
        var protector = new AesOtpCodeProtector(NewKey());
        const string plaintext = "482913";

        var cipher = protector.Encrypt(plaintext);
        var decrypted = protector.Decrypt(cipher);

        Assert.NotEqual(plaintext, cipher);
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_ProducesDifferentCipher_PerCall_DueToRandomIv()
    {
        var protector = new AesOtpCodeProtector(NewKey());

        var c1 = protector.Encrypt("123456");
        var c2 = protector.Encrypt("123456");

        Assert.NotEqual(c1, c2); // rastgele IV nedeniyle farklı
        Assert.Equal("123456", protector.Decrypt(c1));
        Assert.Equal("123456", protector.Decrypt(c2));
    }

    [Fact]
    public void Mask_AlwaysReturnsMaskedLiteral()
    {
        var protector = new AesOtpCodeProtector(NewKey());

        Assert.Equal("[MASKED]", protector.Mask("123456"));
        Assert.Equal("[MASKED]", protector.Mask(""));
        Assert.Equal("[MASKED]", protector.Mask("any-secret-value"));
    }

    [Fact]
    public void Constructor_InvalidKeyLength_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new AesOtpCodeProtector(new byte[16]));
    }
}

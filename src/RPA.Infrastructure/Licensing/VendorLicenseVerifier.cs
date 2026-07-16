using System.Security.Cryptography;
using RPA.Domain.Licensing;

namespace RPA.Infrastructure.Licensing;

public interface IVendorLicenseVerifier
{
    bool Verify(SignedLicenseDocument document);
}

public sealed class VendorLicenseVerifier : IVendorLicenseVerifier, IDisposable
{
    private readonly RSA _publicKey;

    public VendorLicenseVerifier(string publicKeyPem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKeyPem);
        _publicKey = RSA.Create();
        _publicKey.ImportFromPem(publicKeyPem);
    }

    public bool Verify(SignedLicenseDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!string.Equals(document.Algorithm, "RSA-PSS-SHA256", StringComparison.Ordinal))
            return false;

        try
        {
            return _publicKey.VerifyData(
                CanonicalLicenseSerializer.SerializePayload(document.Payload),
                Convert.FromBase64String(document.Signature),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pss);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    public void Dispose() => _publicKey.Dispose();
}

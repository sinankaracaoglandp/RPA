using System.Security.Cryptography;
using System.Text;

namespace RPA.Infrastructure.Licensing;

public sealed record InstallationIdentity(string InstallationId, string PublicKey, string PublicKeyFingerprint);

public interface IInstallationIdentityService
{
    Task<InstallationIdentity> GetOrCreateAsync(CancellationToken cancellationToken = default);
}

public sealed class InstallationIdentityService : IInstallationIdentityService
{
    private readonly IInstallationKeyStore _keyStore;
    private readonly string _productId;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public InstallationIdentityService(IInstallationKeyStore keyStore, string productId)
    {
        _keyStore = keyStore ?? throw new ArgumentNullException(nameof(keyStore));
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        _productId = productId;
    }

    public async Task<InstallationIdentity> GetOrCreateAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var privateKey = await _keyStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            using var rsa = RSA.Create(3072);
            if (privateKey is null)
            {
                privateKey = rsa.ExportPkcs8PrivateKey();
                await _keyStore.SaveAsync(privateKey, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                rsa.ImportPkcs8PrivateKey(privateKey, out _);
            }

            var publicKey = rsa.ExportSubjectPublicKeyInfo();
            var fingerprint = Convert.ToHexString(SHA256.HashData(publicKey));
            var installationId = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(_productId + ":" + fingerprint)));
            return new InstallationIdentity(installationId, Convert.ToBase64String(publicKey), fingerprint);
        }
        finally
        {
            _gate.Release();
        }
    }
}

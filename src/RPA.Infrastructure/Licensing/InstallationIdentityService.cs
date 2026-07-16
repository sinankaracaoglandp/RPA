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
            if (privateKey is null)
            {
                using var generated = RSA.Create(3072);
                var candidate = generated.ExportPkcs8PrivateKey();
                if (await _keyStore.TrySaveAsync(candidate, cancellationToken).ConfigureAwait(false))
                {
                    privateKey = candidate;
                }
                else
                {
                    CryptographicOperations.ZeroMemory(candidate);
                    privateKey = await _keyStore.LoadAsync(cancellationToken).ConfigureAwait(false)
                        ?? throw new IOException("Installation key creation race completed without a persisted winner.");
                }
            }

            try
            {
                using var rsa = RSA.Create();
                rsa.ImportPkcs8PrivateKey(privateKey, out _);
                var publicKey = rsa.ExportSubjectPublicKeyInfo();
                var fingerprint = Convert.ToHexString(SHA256.HashData(publicKey));
                var installationId = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(_productId + ":" + fingerprint)));
                return new InstallationIdentity(installationId, Convert.ToBase64String(publicKey), fingerprint);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(privateKey);
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}

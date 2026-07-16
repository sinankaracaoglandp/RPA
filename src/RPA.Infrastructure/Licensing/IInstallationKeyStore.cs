namespace RPA.Infrastructure.Licensing;

public interface IInstallationKeyStore
{
    Task<byte[]?> LoadAsync(CancellationToken cancellationToken = default);
    Task<bool> TrySaveAsync(byte[] privateKey, CancellationToken cancellationToken = default);
}

namespace RPA.Infrastructure.Licensing;

public interface IInstallationKeyStore
{
    Task<byte[]?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(byte[] privateKey, CancellationToken cancellationToken = default);
}

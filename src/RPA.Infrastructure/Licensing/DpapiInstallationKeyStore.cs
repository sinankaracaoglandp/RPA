using System.Security.Cryptography;

namespace RPA.Infrastructure.Licensing;

public interface IInstallationKeyProtection
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] protectedData);
}

public interface IInstallationFileSystem
{
    bool Exists(string path);
    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken);
    Task WriteAllBytesAsync(string path, byte[] contents, CancellationToken cancellationToken);
    void CreateDirectory(string path);
    bool TryMoveAtomically(string temporaryPath, string destinationPath);
    void Delete(string path);
}

public sealed class DpapiInstallationKeyStore : IInstallationKeyStore
{
    private const string FileName = "installation-key.pk8.protected";
    private readonly string _directory;
    private readonly string _path;
    private readonly IInstallationFileSystem _files;
    private readonly IInstallationKeyProtection _protection;

    public DpapiInstallationKeyStore(string applicationDataDirectory)
        : this(applicationDataDirectory, new PhysicalInstallationFileSystem(), new LocalMachineDpapiProtection()) { }

    public DpapiInstallationKeyStore(
        string applicationDataDirectory,
        IInstallationFileSystem files,
        IInstallationKeyProtection protection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDataDirectory);
        _directory = applicationDataDirectory;
        _path = Path.Combine(applicationDataDirectory, FileName);
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _protection = protection ?? throw new ArgumentNullException(nameof(protection));
    }

    public async Task<byte[]?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!_files.Exists(_path))
            return null;

        var protectedKey = await _files.ReadAllBytesAsync(_path, cancellationToken).ConfigureAwait(false);
        try
        {
            return _protection.Unprotect(protectedKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedKey);
        }
    }

    public async Task<bool> TrySaveAsync(byte[] privateKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        _files.CreateDirectory(_directory);
        var temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        var protectedKey = _protection.Protect(privateKey);
        try
        {
            await _files.WriteAllBytesAsync(temporaryPath, protectedKey, cancellationToken).ConfigureAwait(false);
            return _files.TryMoveAtomically(temporaryPath, _path);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedKey);
            _files.Delete(temporaryPath);
        }
    }
}

internal sealed class LocalMachineDpapiProtection : IInstallationKeyProtection
{
    public byte[] Protect(byte[] plaintext) =>
        ProtectedData.Protect(plaintext, null, DataProtectionScope.LocalMachine);

    public byte[] Unprotect(byte[] protectedData) =>
        ProtectedData.Unprotect(protectedData, null, DataProtectionScope.LocalMachine);
}

internal sealed class PhysicalInstallationFileSystem : IInstallationFileSystem
{
    public bool Exists(string path) => File.Exists(path);
    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken) =>
        File.ReadAllBytesAsync(path, cancellationToken);
    public Task WriteAllBytesAsync(string path, byte[] contents, CancellationToken cancellationToken) =>
        File.WriteAllBytesAsync(path, contents, cancellationToken);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public bool TryMoveAtomically(string temporaryPath, string destinationPath)
    {
        try
        {
            File.Move(temporaryPath, destinationPath, overwrite: false);
            return true;
        }
        catch (IOException) when (File.Exists(destinationPath))
        {
            return false;
        }
    }

    public void Delete(string path) => File.Delete(path);
}

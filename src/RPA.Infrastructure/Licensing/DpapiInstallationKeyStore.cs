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
    void MoveAtomically(string temporaryPath, string destinationPath);
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
        return _protection.Unprotect(protectedKey);
    }

    public async Task SaveAsync(byte[] privateKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        _files.CreateDirectory(_directory);
        var temporaryPath = _path + ".tmp";
        var protectedKey = _protection.Protect(privateKey);
        await _files.WriteAllBytesAsync(temporaryPath, protectedKey, cancellationToken).ConfigureAwait(false);
        _files.MoveAtomically(temporaryPath, _path);
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
    public void MoveAtomically(string temporaryPath, string destinationPath) =>
        File.Move(temporaryPath, destinationPath, overwrite: true);
}

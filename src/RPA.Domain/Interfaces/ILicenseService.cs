using RPA.Domain.Licensing;

namespace RPA.Domain.Interfaces;

public interface ILicenseService
{
    Task<LicenseStatus> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<InstallationRequestDocument> ExportInstallationRequestAsync(CancellationToken cancellationToken = default);
    Task<LicenseStatus> ImportAsync(SignedLicenseDocument document, CancellationToken cancellationToken = default);
    Task EnsureAgentCapacityAsync(CancellationToken cancellationToken = default);
}

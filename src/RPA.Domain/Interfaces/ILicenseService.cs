using RPA.Domain.Entities;
using RPA.Domain.Licensing;

namespace RPA.Domain.Interfaces;

public interface ILicenseService
{
    Task<LicenseStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Bu kurulumun LicenseInstallation kaydi (yoksa null). Cagiranlar kurulum satirini kendileri
    /// sorgulamak yerine bunu kullanir — kurulum kimligi tek yerde cozulur.
    /// </summary>
    Task<LicenseInstallation?> GetCurrentInstallationAsync(CancellationToken cancellationToken = default);
    Task<InstallationRequestDocument> ExportInstallationRequestAsync(CancellationToken cancellationToken = default);
    Task<LicenseStatus> ImportAsync(SignedLicenseDocument document, CancellationToken cancellationToken = default);
    Task EnsureAgentCapacityAsync(CancellationToken cancellationToken = default);
}

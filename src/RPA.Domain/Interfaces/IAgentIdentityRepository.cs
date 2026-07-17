using RPA.Domain.Entities;

namespace RPA.Domain.Interfaces;

public interface IAgentIdentityRepository
{
    Task<AgentIdentity> CreateAsync(AgentIdentity identity, CancellationToken cancellationToken = default);
    Task<AgentIdentity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AgentIdentity?> GetByMachineFingerprintAsync(Guid licenseInstallationId, string machineFingerprint, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AgentIdentity>> ListAsync(Guid licenseInstallationId, CancellationToken cancellationToken = default);
    Task ActivateAsync(Guid id, string machineFingerprint, string credentialHash, DateTimeOffset activatedAt, CancellationToken cancellationToken = default);
    Task DisableAsync(Guid id, DateTimeOffset disabledAt, CancellationToken cancellationToken = default);
    Task DeactivateAsync(Guid id, DateTimeOffset deactivatedAt, CancellationToken cancellationToken = default);
    Task RotateCredentialAsync(Guid id, string credentialHash, CancellationToken cancellationToken = default);
}

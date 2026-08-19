using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Exceptions;
using RPA.Domain.Interfaces;
using RPA.Domain.Licensing;
using RPA.Infrastructure.Licensing;

namespace RPA.Infrastructure.Persistence.Repositories;

public sealed class EfAgentIdentityRepository : IAgentIdentityRepository
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> NonPostgresLocks = new();
    private readonly RpaDbContext _db;
    private readonly bool _licenseEnforced;

    /// <param name="licenseEnforced">
    /// false ise aktivasyon lisans belgesi/son kullanma/koltuk sinirini uygulamaz
    /// (yalnizca DEBUG derlemesindeki gelistirme bypass'i; bkz. <see cref="Licensing.DevelopmentLicenseBypass"/>).
    /// Aktivasyon KODU dogrulamasi her durumda uygulanir — o kimlik dogrulamadir, lisanslama degil.
    /// </param>
    public EfAgentIdentityRepository(RpaDbContext db, bool licenseEnforced = true)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _licenseEnforced = licenseEnforced;
    }

    public async Task<AgentIdentity> CreateAsync(AgentIdentity identity, CancellationToken cancellationToken = default)
    { _db.AgentIdentities.Add(identity); await _db.SaveChangesAsync(cancellationToken); return identity; }
    public Task<AgentIdentity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _db.AgentIdentities.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, cancellationToken);
    public Task<AgentIdentity?> GetByMachineFingerprintAsync(Guid licenseInstallationId, string machineFingerprint, CancellationToken cancellationToken = default) =>
        _db.AgentIdentities.SingleOrDefaultAsync(x => x.LicenseInstallationId == licenseInstallationId && x.MachineFingerprint == machineFingerprint && !x.IsDeleted, cancellationToken);
    public async Task<IReadOnlyList<AgentIdentity>> ListAsync(Guid licenseInstallationId, CancellationToken cancellationToken = default) =>
        await _db.AgentIdentities.Where(x => x.LicenseInstallationId == licenseInstallationId && !x.IsDeleted).ToListAsync(cancellationToken);

    public Task ActivateAsync(Guid id, string machineFingerprint, string credentialHash, DateTimeOffset activatedAt, CancellationToken cancellationToken = default) =>
        ActivateCoreAsync(null, id, machineFingerprint, credentialHash, activatedAt, cancellationToken);

    public Task ActivateWithCodeAsync(Guid licenseInstallationId, string activationCodeHash, Guid id,
        string machineFingerprint, string credentialHash, DateTimeOffset activatedAt, CancellationToken cancellationToken = default) =>
        ActivateCoreAsync((licenseInstallationId, activationCodeHash), id, machineFingerprint, credentialHash, activatedAt, cancellationToken);

    private async Task ActivateCoreAsync((Guid InstallationId, string Hash)? code, Guid id, string machineFingerprint,
        string credentialHash, DateTimeOffset activatedAt, CancellationToken cancellationToken)
    {
        var installationId = code?.InstallationId ?? (await GetByIdAsync(id, cancellationToken))?.LicenseInstallationId
            ?? throw new BusinessException("AGENT_NOT_FOUND");
        var provider = _db.Database.ProviderName ?? "";
        var fallbackGate = provider.Contains("Npgsql", StringComparison.Ordinal) ? null : NonPostgresLocks.GetOrAdd(installationId, _ => new(1, 1));
        if (fallbackGate is not null) await fallbackGate.WaitAsync(cancellationToken);
        IDbContextTransaction? transaction = null;
        try
        {
            transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            LicenseInstallation installation;
            if (provider.Contains("Npgsql", StringComparison.Ordinal))
                installation = await _db.LicenseInstallations.FromSqlInterpolated(
                    $"SELECT * FROM \"LicenseInstallations\" WHERE \"Id\" = {installationId} FOR UPDATE").SingleAsync(cancellationToken);
            else
                installation = await _db.LicenseInstallations.SingleAsync(x => x.Id == installationId, cancellationToken);

            var document = installation.SignedLicenseDocument is null
                ? null : LicenseDocumentJson.Deserialize(installation.SignedLicenseDocument);
            if (_licenseEnforced)
            {
                if (document is null) throw new BusinessException("LICENSE_MISSING");
                if (document.Payload.ExpiresAt <= activatedAt) throw new BusinessException("LICENSE_EXPIRED");
            }

            var agent = await _db.AgentIdentities.SingleOrDefaultAsync(x => x.Id == id && x.LicenseInstallationId == installationId && !x.IsDeleted, cancellationToken)
                ?? throw new BusinessException("AGENT_NOT_FOUND");
            AgentActivation? activation = null;
            if (code.HasValue)
            {
                activation = await _db.AgentActivations.SingleOrDefaultAsync(x => x.AgentIdentityId == id && x.ActivationCodeHash == code.Value.Hash && x.ConsumedAt == null && !x.IsDeleted, cancellationToken);
                if (activation is null) throw new BusinessException("ACTIVATION_CODE_INVALID");
                if (activation.ExpiresAt <= activatedAt) throw new BusinessException("ACTIVATION_CODE_EXPIRED");
            }

            if (document is not null)
            {
                var used = await _db.AgentIdentities.CountAsync(x => x.LicenseInstallationId == installationId && !x.IsDeleted &&
                    (x.Status == AgentIdentityStatus.Activated || x.Status == AgentIdentityStatus.Disabled), cancellationToken);
                if (!agent.Status.ConsumesSeat() && used >= document.Payload.MaxActivatedAgents)
                    throw new BusinessException("AGENT_LICENSE_LIMIT_REACHED");
            }

            agent.Status = AgentIdentityStatus.Activated;
            agent.MachineFingerprint = machineFingerprint;
            agent.CredentialHash = credentialHash;
            agent.ActivatedAt = activatedAt;
            agent.DisabledAt = null;
            agent.DeactivatedAt = null;
            if (activation is not null) activation.ConsumedAt = activatedAt;
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
            fallbackGate?.Release();
        }
    }

    public Task DisableAsync(Guid id, DateTimeOffset disabledAt, CancellationToken cancellationToken = default) => ChangeStatusAsync(id, AgentIdentityStatus.Disabled, disabledAt, cancellationToken);
    public Task DeactivateAsync(Guid id, DateTimeOffset deactivatedAt, CancellationToken cancellationToken = default) => ChangeStatusAsync(id, AgentIdentityStatus.Deactivated, deactivatedAt, cancellationToken);
    private async Task ChangeStatusAsync(Guid id, AgentIdentityStatus status, DateTimeOffset at, CancellationToken ct)
    {
        var agent = await RequireAsync(id, ct); agent.Status = status;
        if (status == AgentIdentityStatus.Disabled) agent.DisabledAt = at;
        else { agent.DeactivatedAt = at; agent.CredentialHash = null; }
        await _db.SaveChangesAsync(ct);
    }
    public async Task RotateCredentialAsync(Guid id, string credentialHash, CancellationToken cancellationToken = default)
    { var agent = await RequireAsync(id, cancellationToken); agent.CredentialHash = credentialHash; await _db.SaveChangesAsync(cancellationToken); }

    /// <summary>
    /// Agent'i getirir; yoksa AGENT_NOT_FOUND (BusinessException) atar. Silinmis kayitlar da
    /// yok sayilir — aksi halde GetByIdAsync (!IsDeleted) null derken mutasyon yollari ayni
    /// satiri gunceller, ve eksik kayitta ham InvalidOperationException 500'e donusurdu.
    /// </summary>
    private async Task<AgentIdentity> RequireAsync(Guid id, CancellationToken ct) =>
        await _db.AgentIdentities.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
            ?? throw new BusinessException("AGENT_NOT_FOUND");
}

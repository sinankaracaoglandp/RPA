using Microsoft.EntityFrameworkCore;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Exceptions;
using RPA.Domain.Interfaces;
using RPA.Domain.Licensing;
using RPA.Infrastructure.Persistence;

namespace RPA.Infrastructure.Licensing;

public sealed class LicenseService : ILicenseService
{
    private readonly RpaDbContext _db;
    private readonly IInstallationIdentityService _identityService;
    private readonly IVendorLicenseVerifier _verifier;
    private readonly string _productId;
    private readonly string? _customerReference;

    public LicenseService(RpaDbContext db, IInstallationIdentityService identityService,
        IVendorLicenseVerifier verifier, string productId, string? customerReference = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _identityService = identityService ?? throw new ArgumentNullException(nameof(identityService));
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        _productId = productId;
        _customerReference = customerReference;
    }

    public async Task<InstallationRequestDocument> ExportInstallationRequestAsync(CancellationToken cancellationToken = default)
    {
        var identity = await _identityService.GetOrCreateAsync(cancellationToken);
        var installation = await _db.LicenseInstallations.SingleOrDefaultAsync(x => x.InstallationId == identity.InstallationId, cancellationToken);
        if (installation is null)
        {
            installation = new LicenseInstallation
            {
                InstallationId = identity.InstallationId, PublicKey = identity.PublicKey,
                PublicKeyFingerprint = identity.PublicKeyFingerprint, ProductId = _productId,
                CustomerReference = _customerReference, InstallationCreatedAt = DateTimeOffset.UtcNow
            };
            _db.Add(installation);
            await _db.SaveChangesAsync(cancellationToken);
        }
        return new InstallationRequestDocument(1, installation.InstallationId, installation.PublicKey,
            installation.PublicKeyFingerprint, installation.ProductId, installation.CustomerReference, installation.InstallationCreatedAt);
    }

    public async Task<LicenseStatus> ImportAsync(SignedLicenseDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!_verifier.Verify(document)) throw new BusinessException("LICENSE_SIGNATURE_INVALID");
        var request = await ExportInstallationRequestAsync(cancellationToken);
        if (!string.Equals(document.Payload.InstallationId, request.InstallationId, StringComparison.Ordinal) ||
            !string.Equals(document.Payload.InstallationPublicKeyFingerprint, request.InstallationPublicKeyFingerprint, StringComparison.Ordinal))
            throw new BusinessException("LICENSE_INSTALLATION_MISMATCH");
        if (document.Payload.ExpiresAt <= DateTimeOffset.UtcNow) throw new BusinessException("LICENSE_EXPIRED");
        var installation = await _db.LicenseInstallations.SingleAsync(x => x.InstallationId == request.InstallationId, cancellationToken);
        if (installation.InstalledLicenseRevision is int revision && document.Payload.Revision <= revision)
            throw new BusinessException("LICENSE_REVISION_INVALID");
        installation.SignedLicenseDocument = LicenseDocumentJson.Serialize(document);
        installation.InstalledLicenseRevision = document.Payload.Revision;
        await _db.SaveChangesAsync(cancellationToken);
        return await GetStatusAsync(cancellationToken);
    }

    public async Task<LicenseStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var installation = await _db.LicenseInstallations.SingleOrDefaultAsync(x => !x.IsDeleted, cancellationToken);
        if (installation?.SignedLicenseDocument is null)
            return new(false, false, null, null, null, null, null, null, 0, 0, [], "LICENSE_MISSING");
        var document = LicenseDocumentJson.Deserialize(installation.SignedLicenseDocument);
        if (document is null || !_verifier.Verify(document))
            return new(true, false, null, installation.InstalledLicenseRevision, null, null, null, null, 0, 0, [], "LICENSE_SIGNATURE_INVALID");
        var used = await _db.AgentIdentities.CountAsync(x => x.LicenseInstallationId == installation.Id && !x.IsDeleted &&
            (x.Status == AgentIdentityStatus.Activated || x.Status == AgentIdentityStatus.Disabled), cancellationToken);
        var valid = document.Payload.ExpiresAt > DateTimeOffset.UtcNow;
        return new(true, valid, document.Payload.LicenseId, document.Payload.Revision, document.Payload.CustomerId,
            document.Payload.CustomerName, document.Payload.Edition, document.Payload.ExpiresAt, document.Payload.MaxActivatedAgents, used, document.Payload.Features,
            valid ? null : "LICENSE_EXPIRED");
    }

    public async Task EnsureAgentCapacityAsync(CancellationToken cancellationToken = default)
    {
        var status = await GetStatusAsync(cancellationToken);
        if (!status.IsInstalled) throw new BusinessException("LICENSE_MISSING");
        if (!status.IsValid) throw new BusinessException(status.ErrorCode ?? "LICENSE_INVALID");
        if (status.ActivatedAgents >= status.MaxActivatedAgents) throw new BusinessException("AGENT_LICENSE_LIMIT_REACHED");
    }
}

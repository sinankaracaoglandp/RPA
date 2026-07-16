namespace RPA.Infrastructure.Services;

using Microsoft.EntityFrameworkCore;
using RPA.Application.EInvoiceProfiles;
using RPA.Domain.Entities;
using RPA.Domain.Exceptions;

public sealed class EInvoiceProfileService
{
    private readonly Persistence.RpaDbContext _db;
    private readonly EInvoiceProfileDefinitionValidator _validator;

    public EInvoiceProfileService(Persistence.RpaDbContext db, EInvoiceProfileDefinitionValidator validator)
    {
        _db = db;
        _validator = validator;
    }

    public async Task<List<EInvoiceProfile>> ListAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await RequireProjectAsync(projectId, cancellationToken);
        return await _db.EInvoiceProfiles.Where(x => x.ProjectId == projectId)
            .OrderBy(x => x.Name).ToListAsync(cancellationToken);
    }

    public async Task<EInvoiceProfile> CreateAsync(Guid projectId, string name, string? description, CancellationToken cancellationToken = default)
    {
        await RequireProjectAsync(projectId, cancellationToken);
        var normalized = name?.Trim() ?? string.Empty;
        if (normalized.Length == 0) throw new BusinessException("Profil adı zorunludur.");
        if (await _db.EInvoiceProfiles.AnyAsync(x => x.ProjectId == projectId && x.Name == normalized, cancellationToken))
            throw new BusinessException("Aynı isimde bir e-fatura profili zaten var.");
        var profile = new EInvoiceProfile { ProjectId = projectId, Name = normalized, Description = description?.Trim() };
        _db.EInvoiceProfiles.Add(profile);
        await _db.SaveChangesAsync(cancellationToken);
        return profile;
    }

    public async Task<EInvoiceProfile> GetAsync(Guid projectId, Guid profileId, CancellationToken cancellationToken = default) =>
        await _db.EInvoiceProfiles.Include(x => x.Versions)
            .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Id == profileId, cancellationToken)
        ?? throw new BusinessException("E-fatura profili bulunamadı.");

    public async Task<EInvoiceProfile> SaveDraftAsync(Guid projectId, Guid profileId, string definitionJson, CancellationToken cancellationToken = default)
    {
        _validator.ParseAndValidate(definitionJson);
        var profile = await GetAsync(projectId, profileId, cancellationToken);
        profile.DraftDefinitionJson = definitionJson;
        profile.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return profile;
    }

    public async Task<EInvoiceProfileVersion> PublishAsync(Guid projectId, Guid profileId, Guid? publishedBy, CancellationToken cancellationToken = default)
    {
        var profile = await GetAsync(projectId, profileId, cancellationToken);
        var schema = _validator.ValidateAndBuildSchema(profile.DraftDefinitionJson);
        var version = new EInvoiceProfileVersion
        {
            ProfileId = profile.Id,
            Version = profile.Versions.Count == 0 ? 1 : profile.Versions.Max(x => x.Version) + 1,
            DefinitionJson = profile.DraftDefinitionJson,
            OutputSchemaJson = schema,
            PublishedAt = DateTime.UtcNow,
            PublishedBy = publishedBy,
        };
        _db.EInvoiceProfileVersions.Add(version);
        await _db.SaveChangesAsync(cancellationToken);
        return version;
    }

    public async Task<List<EInvoiceProfileVersion>> ListVersionsAsync(Guid projectId, Guid profileId, CancellationToken cancellationToken = default)
    {
        var profile = await GetAsync(projectId, profileId, cancellationToken);
        return profile.Versions.OrderByDescending(x => x.Version).ToList();
    }

    public async Task<EInvoiceProfileVersion> GetVersionAsync(Guid projectId, Guid profileId, int version, CancellationToken cancellationToken = default)
    {
        var profile = await GetAsync(projectId, profileId, cancellationToken);
        return profile.Versions.SingleOrDefault(x => x.Version == version)
            ?? throw new BusinessException("E-fatura profil sürümü bulunamadı.");
    }

    public async Task DeleteAsync(Guid projectId, Guid profileId, CancellationToken cancellationToken = default)
    {
        var profile = await GetAsync(projectId, profileId, cancellationToken);
        profile.IsDeleted = true;
        profile.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task RequireProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        if (!await _db.Projects.AnyAsync(x => x.Id == projectId && !x.IsDeleted, cancellationToken))
            throw new BusinessException("Proje bulunamadı.");
    }
}

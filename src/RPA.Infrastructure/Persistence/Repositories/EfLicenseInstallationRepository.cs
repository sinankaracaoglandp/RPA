using Microsoft.EntityFrameworkCore;
using RPA.Domain.Entities;

namespace RPA.Infrastructure.Persistence.Repositories;

public sealed class EfLicenseInstallationRepository(RpaDbContext db)
{
    public Task<LicenseInstallation?> GetAsync(CancellationToken cancellationToken = default) =>
        db.LicenseInstallations.SingleOrDefaultAsync(x => !x.IsDeleted, cancellationToken);

    public async Task<LicenseInstallation> SaveAsync(LicenseInstallation installation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installation);
        db.Update(installation);
        await db.SaveChangesAsync(cancellationToken);
        return installation;
    }
}

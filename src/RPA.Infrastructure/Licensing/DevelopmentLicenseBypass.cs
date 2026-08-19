using Microsoft.Extensions.Configuration;
using RPA.Domain.Entities;
using RPA.Domain.Interfaces;
using RPA.Domain.Licensing;

namespace RPA.Infrastructure.Licensing;

/// <summary>
/// Gelistirme (DEBUG) derlemesinde lisans anahtari zorunlulugunu kaldiran anahtar.
///
/// KURAL: bypass YALNIZCA DEBUG derlemede derlenir. RELEASE derlemede <see cref="IsEnabled"/>
/// her zaman <c>false</c> dondurur ve hicbir yapilandirma degeri bunu acamaz — aksi halde
/// unutulmus tek bir ayar tum lisanslamayi urunde devre disi birakabilirdi.
///
/// DEBUG'da varsayilan ACIKTIR (gelistirici hicbir sey yapilandirmadan calisabilsin);
/// <c>Licensing:DevelopmentBypass=false</c> ile kapatilabilir — lisans davranisini olcen
/// testler bunu yapar.
/// </summary>
public static class DevelopmentLicenseBypass
{
    public const string ConfigurationKey = "Licensing:DevelopmentBypass";

    public static bool IsEnabled(IConfiguration? configuration)
    {
#if DEBUG
        var value = configuration?[ConfigurationKey];
        return string.IsNullOrWhiteSpace(value) || !bool.TryParse(value, out var enabled) || enabled;
#else
        _ = configuration;
        return false;
#endif
    }
}

/// <summary>
/// <see cref="ILicenseService"/> dekoratoru: gercek bir lisans belgesi YOKKEN (veya gecersizken)
/// gelistirme lisansi gibi davranir. Gercek ve gecerli bir lisans yuklenmisse ona dokunmaz —
/// gelistirme makinesinde gercek lisansla test etmek mumkun kalir.
///
/// Yalnizca <see cref="DevelopmentLicenseBypass.IsEnabled"/> true iken kaydedilir.
/// </summary>
public sealed class DevelopmentLicenseService : ILicenseService
{
    /// <summary>Gelistirme lisansinin gorunen sürumu (Studio lisans ekraninda goruntulenir).</summary>
    public const string DevelopmentEdition = "development";

    private readonly ILicenseService _inner;

    public DevelopmentLicenseService(ILicenseService inner) =>
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public async Task<LicenseStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var status = await _inner.GetStatusAsync(cancellationToken);
        if (status.IsInstalled && status.IsValid) return status;

        var installation = await GetCurrentInstallationAsync(cancellationToken);
        return new LicenseStatus(
            isInstalled: true,
            isValid: true,
            licenseId: "DEV-LOCAL",
            revision: 0,
            customerId: "DEV",
            customerName: "Gelistirme (lisanssiz DEBUG derlemesi)",
            edition: DevelopmentEdition,
            expiresAt: null,
            maxActivatedAgents: int.MaxValue,
            activatedAgents: status.ActivatedAgents,
            features: ["agent"],
            errorCode: null);
    }

    /// <summary>
    /// Kurulum satiri yoksa olusturur: lisans ice aktarilmadigi icin satir hic yaratilmamis olur
    /// ve agent olusturma/aktivasyon uclari kuruluma bagli calisir.
    /// </summary>
    public async Task<LicenseInstallation?> GetCurrentInstallationAsync(CancellationToken cancellationToken = default)
    {
        var installation = await _inner.GetCurrentInstallationAsync(cancellationToken);
        if (installation is not null) return installation;

        await _inner.ExportInstallationRequestAsync(cancellationToken);
        return await _inner.GetCurrentInstallationAsync(cancellationToken);
    }

    public Task<InstallationRequestDocument> ExportInstallationRequestAsync(CancellationToken cancellationToken = default) =>
        _inner.ExportInstallationRequestAsync(cancellationToken);

    /// <summary>Gercek lisans ice aktarimi bypass'ta da calisir (imza dogrulamasi degismez).</summary>
    public Task<LicenseStatus> ImportAsync(SignedLicenseDocument document, CancellationToken cancellationToken = default) =>
        _inner.ImportAsync(document, cancellationToken);

    /// <summary>Gelistirmede koltuk siniri uygulanmaz.</summary>
    public Task EnsureAgentCapacityAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

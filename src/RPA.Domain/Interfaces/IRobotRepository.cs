namespace RPA.Domain.Interfaces;

using RPA.Domain.Entities;

/// <summary>
/// Robot varlığı için kalıcılık erişimi (Spec Bölüm 5.6). Robot kayıt/heartbeat/offline
/// tespiti servislerinin veri erişim sözleşmesi.
/// </summary>
public interface IRobotRepository
{
    /// <summary>Verilen makine adına sahip (soft-delete edilmemiş) robotu bulur; yoksa null.</summary>
    Task<Robot?> FindByMachineNameAsync(string machineName, CancellationToken cancellationToken = default);

    /// <summary>Id ile robotu bulur; yoksa null.</summary>
    Task<Robot?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Son heartbeat'i verilen eşikten (UTC) daha eski olan Offline olmayan robotları döner.</summary>
    Task<IReadOnlyList<Robot>> FindStaleAsync(DateTime olderThanUtc, CancellationToken cancellationToken = default);

    /// <summary>Tüm robotları döner (Orchestrator Robotlar ekranı — WP-6.1).</summary>
    Task<IReadOnlyList<Robot>> ListAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Yeni robot ekler (henüz kalıcı değil; SaveChangesAsync gerekir).</summary>
    Task AddAsync(Robot robot, CancellationToken cancellationToken = default);

    /// <summary>Bekleyen değişiklikleri kalıcı hale getirir.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

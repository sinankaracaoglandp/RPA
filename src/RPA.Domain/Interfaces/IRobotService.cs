namespace RPA.Domain.Interfaces;

using RPA.Domain.Entities;
using RPA.Domain.Enums;

/// <summary>
/// Robot yaşam döngüsü servisi (Spec Bölüm 5.6, 9): kayıt, sorgulama, heartbeat ve
/// offline tespiti. Orchestrator, robot ajanlarının durumunu bu servis üzerinden yönetir.
/// </summary>
public interface IRobotService
{
    /// <summary>
    /// Robotu kaydeder. Aynı makine adı zaten varsa mevcut kayıt güncellenir (idempotent),
    /// aksi halde yeni bir Robot (Status = Online) oluşturulur.
    /// </summary>
    Task<Robot> RegisterAsync(RobotRegistrationRequest request, CancellationToken cancellationToken = default);

    /// <summary>Id ile robot döner; yoksa null.</summary>
    Task<Robot?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Tüm robotları döner (Orchestrator Robotlar ekranı — WP-6.1).</summary>
    Task<IReadOnlyList<Robot>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Robotun heartbeat'ini kaydeder: LastHeartbeat = şimdi (UTC), Status = Online.
    /// Robot bulunamazsa null döner.
    /// </summary>
    Task<Robot?> RecordHeartbeatAsync(Guid robotId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Son heartbeat'i verilen zaman aşımından daha eski olan robotları Offline işaretler.
    /// Offline'a çekilen robot sayısını döner (Spec Bölüm 9 — heartbeat timeout = 5 dk).
    /// </summary>
    Task<int> DetectOfflineRobotsAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}

/// <summary>Robot kayıt isteği (Spec Bölüm 5.6).</summary>
public sealed class RobotRegistrationRequest
{
    public string MachineName { get; set; } = "";
    public RobotMode Mode { get; set; } = RobotMode.Unattended;
    public string Tags { get; set; } = "";
    public string? AgentVersion { get; set; }
    public int Capacity { get; set; } = 1;
}

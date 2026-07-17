namespace RPA.Infrastructure.Robots;

using Microsoft.Extensions.Logging;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Exceptions;
using RPA.Domain.Interfaces;

/// <summary>
/// Robot yaşam döngüsü servisi (Spec Bölüm 5.6, 9). Kayıt, sorgulama, heartbeat ve
/// offline tespiti sağlar. Correlation ID = robot heartbeat oturumu (robot Id).
/// </summary>
public sealed class RobotService : IRobotService
{
    private readonly IRobotRepository _repository;
    private readonly ILogger<RobotService> _logger;

    public RobotService(IRobotRepository repository, ILogger<RobotService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Robot> RegisterAsync(RobotRegistrationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.MachineName))
            throw new ArgumentException("MachineName zorunludur.", nameof(request));

        var existing = await _repository.FindByMachineNameAsync(request.MachineName, cancellationToken);
        if (existing is not null)
        {
            // Makine adi idempotent anahtardir: ayni ad ile yeniden kayit mevcut robotu gunceller.
            // Bu yuzden sahiplik BURADA korunmalidir — aksi halde baska bir ajan, kurbanin makine
            // adiyla kaydolup onun robot kaydini (ve dolayisiyla is akisini) devralabilirdi.
            EnsureOwnership(existing, request.AgentIdentityId);
            existing.AgentIdentityId = request.AgentIdentityId ?? existing.AgentIdentityId;
            existing.Mode = request.Mode;
            existing.Tags = request.Tags;
            existing.AgentVersion = request.AgentVersion;
            existing.Capacity = request.Capacity;
            existing.Status = RobotStatus.Online;
            existing.LastHeartbeat = DateTime.UtcNow;
            await _repository.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Robot {RobotId} ({MachineName}) yeniden kaydedildi.", existing.Id, existing.MachineName);
            return existing;
        }

        var robot = new Robot
        {
            MachineName = request.MachineName,
            AgentIdentityId = request.AgentIdentityId,
            Mode = request.Mode,
            Tags = request.Tags,
            AgentVersion = request.AgentVersion,
            Capacity = request.Capacity,
            Status = RobotStatus.Online,
            LastHeartbeat = DateTime.UtcNow,
        };
        await _repository.AddAsync(robot, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Robot {RobotId} ({MachineName}) kaydedildi.", robot.Id, robot.MachineName);
        return robot;
    }

    /// <summary>
    /// Caginin (ajanin) bu robotun sahibi oldugunu dogrular; degilse BusinessException atar.
    /// </summary>
    /// <remarks>
    /// <paramref name="callerAgentIdentityId"/> null ise kontrol UYGULANMAZ: bu, sunucu-ici
    /// cagri demektir (ajan kimligi tasimayan yollar). Ajan uzerinden gelen her cagri kimligini
    /// JWT'den tasir, dolayisiyla istemci bu kontrolu null gondererek atlayamaz.
    /// Sahipsiz (AgentIdentityId = null) robotlar ilk sahiplenen ajana baglanir.
    /// </remarks>
    private static void EnsureOwnership(Robot robot, Guid? callerAgentIdentityId)
    {
        if (callerAgentIdentityId is null || robot.AgentIdentityId is null)
        {
            return;
        }

        if (robot.AgentIdentityId != callerAgentIdentityId)
        {
            throw new BusinessException("ROBOT_NOT_OWNED");
        }
    }

    public Task<Robot?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => _repository.FindByIdAsync(id, cancellationToken);

    public Task<IReadOnlyList<Robot>> ListAsync(CancellationToken cancellationToken = default)
        => _repository.ListAllAsync(cancellationToken);

    public async Task<Robot?> RecordHeartbeatAsync(Guid robotId, CancellationToken cancellationToken = default)
        => await RecordHeartbeatAsync(robotId, agentIdentityId: null, cancellationToken);

    public async Task<Robot?> RecordHeartbeatAsync(Guid robotId, Guid? agentIdentityId, CancellationToken cancellationToken = default)
    {
        var robot = await _repository.FindByIdAsync(robotId, cancellationToken);
        if (robot is null)
        {
            _logger.LogWarning("Heartbeat: bilinmeyen robot {RobotId}.", robotId);
            return null;
        }

        // robotId istemciden gelir: caginin gercekten bu robot oldugu dogrulanmalidir.
        EnsureOwnership(robot, agentIdentityId);

        robot.LastHeartbeat = DateTime.UtcNow;
        robot.Status = RobotStatus.Online;
        await _repository.SaveChangesAsync(cancellationToken);
        return robot;
    }

    public async Task<int> DetectOfflineRobotsAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var threshold = DateTime.UtcNow - timeout;
        var stale = await _repository.FindStaleAsync(threshold, cancellationToken);
        foreach (var robot in stale)
        {
            robot.Status = RobotStatus.Offline;
            _logger.LogWarning("Robot {RobotId} ({MachineName}) heartbeat zaman aşımı — Offline.", robot.Id, robot.MachineName);
        }

        if (stale.Count > 0)
            await _repository.SaveChangesAsync(cancellationToken);

        return stale.Count;
    }
}

namespace RPA.Infrastructure.Robots;

using Microsoft.Extensions.Logging;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
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

    public Task<Robot?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => _repository.FindByIdAsync(id, cancellationToken);

    public Task<IReadOnlyList<Robot>> ListAsync(CancellationToken cancellationToken = default)
        => _repository.ListAllAsync(cancellationToken);

    public async Task<Robot?> RecordHeartbeatAsync(Guid robotId, CancellationToken cancellationToken = default)
    {
        var robot = await _repository.FindByIdAsync(robotId, cancellationToken);
        if (robot is null)
        {
            _logger.LogWarning("Heartbeat: bilinmeyen robot {RobotId}.", robotId);
            return null;
        }

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

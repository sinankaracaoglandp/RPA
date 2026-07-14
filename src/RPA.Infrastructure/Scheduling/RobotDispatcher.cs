namespace RPA.Infrastructure.Scheduling;

using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;

/// <summary>
/// Tag havuzu tabanlı ajan seçici. Online + kapasitesi boş + Trigger tag'lerini kapsayan robotlar
/// arasından en boş kapasiteli → en yüksek Trigger.Priority → en eski heartbeat olanı seçer.
/// </summary>
public sealed class RobotDispatcher : IRobotDispatcher
{
    private readonly IRobotService _robotService;
    private readonly ITriggerRepository _triggerRepository;

    public RobotDispatcher(IRobotService robotService, ITriggerRepository triggerRepository)
    {
        _robotService = robotService ?? throw new ArgumentNullException(nameof(robotService));
        _triggerRepository = triggerRepository ?? throw new ArgumentNullException(nameof(triggerRepository));
    }

    public async Task<Robot?> SelectRobotAsync(Trigger trigger, CancellationToken cancellationToken = default)
    {
        var required = ParseTags(trigger.TargetRobotTags);
        var robots = await _robotService.ListAsync(cancellationToken);
        var activeCounts = await _triggerRepository.GetActiveJobCountsByRobotAsync(cancellationToken);

        var candidate = robots
            .Where(r => r.Status == RobotStatus.Online)
            .Where(r => required.All(tag => ParseTags(r.Tags).Contains(tag)))
            .Select(r => new
            {
                Robot = r,
                Free = r.Capacity - (activeCounts.TryGetValue(r.Id, out var c) ? c : 0),
            })
            .Where(x => x.Free > 0)
            .OrderByDescending(x => x.Free)
            .ThenByDescending(x => x.Robot.LastHeartbeat ?? DateTime.MinValue) // en taze; Priority Trigger'da tekildir
            .Select(x => x.Robot)
            .FirstOrDefault();

        return candidate;
    }

    private static HashSet<string> ParseTags(string? tags) =>
        (tags ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant())
            .ToHashSet();
}

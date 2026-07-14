namespace RPA.Domain.Interfaces;

using RPA.Domain.Entities;

/// <summary>
/// Bir tetikleyici (job) ateşlendiğinde onu çalıştıracak uygun Robot'u (Unattended ajan) seçer.
/// Aday = Online + kapasitesi müsait + Tags, Trigger.TargetRobotTags'i kapsayan robot.
/// Aday yoksa null (JobRun Pending kalır).
/// </summary>
public interface IRobotDispatcher
{
    Task<Robot?> SelectRobotAsync(Trigger trigger, CancellationToken cancellationToken = default);
}

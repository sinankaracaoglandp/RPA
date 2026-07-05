namespace RPA.WebAPI.Robots;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using RPA.Domain.Interfaces;

/// <summary>
/// Robot ajanları ile çift yönlü SignalR kanalı (Task 3.1, Spec Bölüm 9).
/// Yalnızca kimliği doğrulanmış (JWT) bağlantılar kabul edilir. Robot register/heartbeat
/// sinyalleri ve workflow olay yayınları bu hub üzerinden akar.
/// </summary>
[Authorize]
public class RobotHub : Hub
{
    private readonly IRobotService _robotService;
    private readonly ILogger<RobotHub> _logger;

    public RobotHub(IRobotService robotService, ILogger<RobotHub> logger)
    {
        _robotService = robotService;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Robot SignalR bağlantısı kuruldu: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Robot SignalR bağlantısı kesildi: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>Robot ajanı kaydını hub üzerinden yapar; grup üyeliği robot Id'sidir.</summary>
    public async Task Register(RegisterRobotRequest request)
    {
        var mode = Enum.TryParse<Domain.Enums.RobotMode>(request.Mode, ignoreCase: true, out var m)
            ? m : Domain.Enums.RobotMode.Unattended;
        var robot = await _robotService.RegisterAsync(new RobotRegistrationRequest
        {
            MachineName = request.MachineName,
            Mode = mode,
            Tags = request.Tags ?? "",
            AgentVersion = request.AgentVersion,
            Capacity = request.Capacity <= 0 ? 1 : request.Capacity,
        });

        await Groups.AddToGroupAsync(Context.ConnectionId, robot.Id.ToString());
        await Clients.Caller.SendAsync("Registered", RobotDto.From(robot));
    }

    /// <summary>Robot ajanı heartbeat sinyali gönderir.</summary>
    public async Task Heartbeat(Guid robotId)
    {
        var robot = await _robotService.RecordHeartbeatAsync(robotId);
        if (robot is null)
        {
            await Clients.Caller.SendAsync("HeartbeatRejected", robotId);
            return;
        }

        await Clients.Caller.SendAsync("HeartbeatAck", robot.LastHeartbeat);
    }
}

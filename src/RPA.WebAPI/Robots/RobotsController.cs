namespace RPA.WebAPI.Robots;

using Microsoft.AspNetCore.Mvc;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;

/// <summary>
/// Robot kayıt ve yaşam döngüsü endpoint'leri (Task 3.1, Spec Bölüm 5.6, 9, 12).
/// </summary>
[ApiController]
[Route("api/robots")]
public class RobotsController : ControllerBase
{
    private readonly IRobotService _robotService;

    public RobotsController(IRobotService robotService)
    {
        _robotService = robotService;
    }

    /// <summary>Robotu kaydeder (idempotent — mevcut makine adı güncellenir).</summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(RobotDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register([FromBody] RegisterRobotRequest request, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.MachineName))
            return BadRequest(new { error = "MachineName zorunludur." });

        var mode = Enum.TryParse<RobotMode>(request.Mode, ignoreCase: true, out var m) ? m : RobotMode.Unattended;
        var robot = await _robotService.RegisterAsync(new RobotRegistrationRequest
        {
            MachineName = request.MachineName,
            Mode = mode,
            Tags = request.Tags ?? "",
            AgentVersion = request.AgentVersion,
            Capacity = request.Capacity <= 0 ? 1 : request.Capacity,
        }, ct);

        var dto = RobotDto.From(robot);
        return CreatedAtAction(nameof(Get), new { id = robot.Id }, dto);
    }

    /// <summary>Id ile robot döner.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(RobotDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var robot = await _robotService.GetAsync(id, ct);
        return robot is null ? NotFound() : Ok(RobotDto.From(robot));
    }

    /// <summary>Robot heartbeat'ini kaydeder (LastHeartbeat + Status = Online).</summary>
    [HttpPut("{id:guid}/heartbeat")]
    [ProducesResponseType(typeof(RobotDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Heartbeat(Guid id, CancellationToken ct)
    {
        var robot = await _robotService.RecordHeartbeatAsync(id, ct);
        return robot is null ? NotFound() : Ok(RobotDto.From(robot));
    }
}

public class RegisterRobotRequest
{
    public string MachineName { get; set; } = string.Empty;
    public string Mode { get; set; } = "Unattended";
    public string? Tags { get; set; }
    public string? AgentVersion { get; set; }
    public int Capacity { get; set; } = 1;
}

public class RobotDto
{
    public Guid Id { get; set; }
    public string MachineName { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? LastHeartbeat { get; set; }
    public string? AgentVersion { get; set; }
    public int Capacity { get; set; }

    public static RobotDto From(Robot r) => new()
    {
        Id = r.Id,
        MachineName = r.MachineName,
        Mode = r.Mode.ToString(),
        Status = r.Status.ToString(),
        LastHeartbeat = r.LastHeartbeat,
        AgentVersion = r.AgentVersion,
        Capacity = r.Capacity,
    };
}

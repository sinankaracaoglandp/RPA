namespace RPA.Agent.Registration;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RPA.Agent.Configuration;
using RPA.Agent.State;
using RPA.Domain.Interfaces;

/// <summary>
/// Ajanı Orchestrator'a kaydeder (Spec Bölüm 5.6). Makine adı idempotent anahtardır:
/// aynı makine için tekrar çağrı mevcut robot kaydını günceller. Dönen Robot.Id ajan
/// durumuna yazılır ve tüm sonraki heartbeat/iş atamalarında correlation ID olarak kullanılır.
/// </summary>
public sealed class RobotRegistrar : IRobotRegistrar
{
    private readonly IRobotService _robotService;
    private readonly IAgentState _state;
    private readonly AgentOptions _options;
    private readonly ILogger<RobotRegistrar> _logger;

    public RobotRegistrar(
        IRobotService robotService,
        IAgentState state,
        IOptions<AgentOptions> options,
        ILogger<RobotRegistrar> logger)
    {
        _robotService = robotService ?? throw new ArgumentNullException(nameof(robotService));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Guid> RegisterAsync(CancellationToken cancellationToken = default)
    {
        _state.SetActivity(AgentActivity.Registering);
        var machineName = _options.EffectiveMachineName;

        var request = new RobotRegistrationRequest
        {
            MachineName = machineName,
            Mode = _options.Mode,
            Tags = _options.Tags,
            Capacity = _options.Capacity,
            AgentVersion = typeof(RobotRegistrar).Assembly.GetName().Version?.ToString() ?? "1.0.0",
        };

        var robot = await _robotService.RegisterAsync(request, cancellationToken);
        _state.SetRobotId(robot.Id);
        _state.SetActivity(AgentActivity.Idle);
        _logger.LogInformation(
            "Robot {RobotId} ({MachineName}) Orchestrator'a kaydedildi (Mode={Mode}).",
            robot.Id, machineName, _options.Mode);
        return robot.Id;
    }
}

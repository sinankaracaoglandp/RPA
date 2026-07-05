namespace RPA.Agent.Registration;

/// <summary>Ajanı Orchestrator'a kaydeder ve robot kimliğini döndürür.</summary>
public interface IRobotRegistrar
{
    /// <summary>Kaydı yapar (idempotent) ve atanan robot kimliğini döndürür.</summary>
    Task<Guid> RegisterAsync(CancellationToken cancellationToken = default);
}

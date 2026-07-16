namespace RPA.WebAPI.Licensing;

using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Persistence;

[ApiController]
[Route("api/agents")]
[Authorize(Policy = "LicenseAdministrator")]
public sealed class AgentsController : ControllerBase
{
    private readonly RpaDbContext _db;
    private readonly IAgentIdentityRepository _agents;
    private readonly IAgentActivationCodeStore _activationCodes;

    public AgentsController(RpaDbContext db, IAgentIdentityRepository agents, IAgentActivationCodeStore activationCodes)
    {
        _db = db;
        _agents = agents;
        _activationCodes = activationCodes;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AgentDto>>> List(CancellationToken cancellationToken)
    {
        var installation = await _db.LicenseInstallations.SingleOrDefaultAsync(x => !x.IsDeleted, cancellationToken);
        if (installation is null) return Ok(Array.Empty<AgentDto>());
        var agents = await _agents.ListAsync(installation.Id, cancellationToken);
        return Ok(agents.Select(AgentDto.From).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<AgentDto>> Create([FromBody] CreateAgentRequest request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "Agent adı zorunludur." });
        }

        var installation = await _db.LicenseInstallations.SingleOrDefaultAsync(x => !x.IsDeleted, cancellationToken);
        if (installation is null)
        {
            return BadRequest(new { error = "LICENSE_MISSING" });
        }

        var agent = await _agents.CreateAsync(new AgentIdentity
        {
            LicenseInstallationId = installation.Id,
            Name = request.Name.Trim(),
        }, cancellationToken);

        return Created($"/api/agents/{agent.Id}", AgentDto.From(agent));
    }

    [HttpPost("{id:guid}/activation-code")]
    public async Task<IActionResult> CreateActivationCode(Guid id, CancellationToken cancellationToken)
    {
        var agent = await _agents.GetByIdAsync(id, cancellationToken);
        if (agent is null) return NotFound();

        var code = SecretGenerator.CreateToken();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(15);
        await _activationCodes.CreateAsync(id, SecretHasher.Hash(code), expiresAt, cancellationToken);

        return Ok(new ActivationCodeResponse(id, code, expiresAt));
    }

    [HttpPost("{id:guid}/disable")]
    public async Task<IActionResult> Disable(Guid id, CancellationToken cancellationToken)
    {
        await _agents.DisableAsync(id, DateTimeOffset.UtcNow, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await _agents.DeactivateAsync(id, DateTimeOffset.UtcNow, cancellationToken);
        return NoContent();
    }
}

public sealed record CreateAgentRequest(string Name);
public sealed record ActivationCodeResponse(Guid AgentId, string ActivationCode, DateTimeOffset ExpiresAt);
public sealed record AgentDto(Guid Id, string Name, AgentIdentityStatus Status, string? MachineFingerprint, DateTimeOffset? LastSeenAt)
{
    public static AgentDto From(AgentIdentity agent) =>
        new(agent.Id, agent.Name, agent.Status, agent.MachineFingerprint, agent.LastSeenAt);
}

public interface IAgentActivationCodeStore
{
    Task CreateAsync(Guid agentIdentityId, string activationCodeHash, DateTimeOffset expiresAt, CancellationToken cancellationToken = default);
}

public sealed class EfAgentActivationCodeStore : IAgentActivationCodeStore
{
    private readonly RpaDbContext _db;

    public EfAgentActivationCodeStore(RpaDbContext db) => _db = db;

    public async Task CreateAsync(Guid agentIdentityId, string activationCodeHash, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
    {
        _db.AgentActivations.Add(new AgentActivation
        {
            AgentIdentityId = agentIdentityId,
            ActivationCodeHash = activationCodeHash,
            ExpiresAt = expiresAt,
        });
        await _db.SaveChangesAsync(cancellationToken);
    }
}

internal static class SecretGenerator
{
    public static string CreateToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }
}

internal static class SecretHasher
{
    public static string Hash(string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}

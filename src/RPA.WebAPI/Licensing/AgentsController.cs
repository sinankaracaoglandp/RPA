namespace RPA.WebAPI.Licensing;

using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Exceptions;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Persistence;

[ApiController]
[Route("api/agents")]
[Authorize(Policy = "LicenseAdministrator")]
public sealed class AgentsController : ControllerBase
{
    private readonly ILicenseService _licenses;
    private readonly IAgentIdentityRepository _agents;
    private readonly IAgentActivationCodeStore _activationCodes;

    public AgentsController(ILicenseService licenses, IAgentIdentityRepository agents, IAgentActivationCodeStore activationCodes)
    {
        _licenses = licenses;
        _agents = agents;
        _activationCodes = activationCodes;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AgentDto>>> List(CancellationToken cancellationToken)
    {
        var installation = await _licenses.GetCurrentInstallationAsync(cancellationToken);
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

        var installation = await _licenses.GetCurrentInstallationAsync(cancellationToken);
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

    /// <summary>
    /// Agent credential'ini degistirir (tasarim spec'i: "controlled credential replacement flow").
    /// Yeni credential plaintext olarak YALNIZCA bu yanitta, bir kez doner; kalicilasan tek sey
    /// hash'tir. Eski credential DERHAL gecersizlesir: token degisimi (AgentAuthController.Token)
    /// yalnizca AgentIdentity.CredentialHash ile karsilastirma yapar, hash uzerine yazildigi an
    /// eski deger hicbir yerde eslesmez. (Verilmis JWT'ler kendi 10 dk omurleriyle doler.)
    ///
    /// KURAL: yalnizca `Activated` agent'in credential'i degistirilebilir. Gerekce:
    /// PendingActivation'in henuz credential'i yoktur, Deactivated'in credential'i silinmistir ve
    /// her ikisi de aktivasyon akisindan credential alir; Disabled ise zaten token alamaz
    /// (AgentTokenService AGENT_NOT_ACTIVE atar) — bu durumlarda rotasyon anlamsiz olup
    /// operatore yanlis bir "credential yenilendi" izlenimi verirdi.
    /// </summary>
    [HttpPost("{id:guid}/rotate-credential")]
    public async Task<IActionResult> RotateCredential(Guid id, CancellationToken cancellationToken)
    {
        var agent = await _agents.GetByIdAsync(id, cancellationToken);
        if (agent is null) return NotFound();
        if (agent.Status != AgentIdentityStatus.Activated)
        {
            return Conflict(new { error = "AGENT_NOT_ACTIVATED" });
        }

        // Aktivasyon akisiyla AYNI uretim/hash semasi (tek kaynak: SecretGenerator/SecretHasher).
        var credential = SecretGenerator.CreateToken();
        await _agents.RotateCredentialAsync(id, SecretHasher.Hash(credential), cancellationToken);

        // Plaintext asla loglanmaz/kalicilastirilmaz — yalnizca bu yanit govdesinde doner.
        return Ok(new RotateCredentialResponse(id, credential));
    }

    [HttpPost("{id:guid}/disable")]
    public async Task<IActionResult> Disable(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _agents.DisableAsync(id, DateTimeOffset.UtcNow, cancellationToken);
        }
        catch (BusinessException) { return NotFound(); }
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _agents.DeactivateAsync(id, DateTimeOffset.UtcNow, cancellationToken);
        }
        catch (BusinessException) { return NotFound(); }
        return NoContent();
    }
}

public sealed record CreateAgentRequest(string Name);
public sealed record ActivationCodeResponse(Guid AgentId, string ActivationCode, DateTimeOffset ExpiresAt);
/// <summary>Plaintext credential yalnizca bir kez doner; sunucu tarafinda hash disinda hicbir sey tutulmaz.</summary>
public sealed record RotateCredentialResponse(Guid AgentId, string Credential);
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

namespace RPA.WebAPI.Authentication;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using RPA.Domain.Exceptions;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Authentication;
using RPA.Infrastructure.Persistence;
using RPA.Infrastructure.Persistence.Repositories;
using RPA.WebAPI.Licensing;

[ApiController]
[Route("api/agent-auth")]
public sealed class AgentAuthController : ControllerBase
{
    private readonly RpaDbContext _db;
    private readonly EfAgentIdentityRepository _agents;
    private readonly AgentTokenService _tokens;
    private readonly ILicenseService _licenses;

    public AgentAuthController(RpaDbContext db, EfAgentIdentityRepository agents, AgentTokenService tokens, ILicenseService licenses)
    {
        _db = db;
        _agents = agents;
        _tokens = tokens;
        _licenses = licenses;
    }

    [AllowAnonymous]
    [HttpPost("activate")]
    public async Task<IActionResult> Activate([FromBody] ActivateAgentRequest request, CancellationToken cancellationToken)
    {
        if (request is null ||
            request.AgentId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.InstallationId) ||
            string.IsNullOrWhiteSpace(request.ActivationCode) ||
            string.IsNullOrWhiteSpace(request.MachineFingerprint))
        {
            return BadRequest(new { error = "ACTIVATION_REQUEST_INVALID" });
        }

        var installation = await _db.LicenseInstallations.SingleOrDefaultAsync(
            x => x.InstallationId == request.InstallationId && !x.IsDeleted, cancellationToken);
        if (installation is null) return Unauthorized(new { error = "LICENSE_INSTALLATION_MISMATCH" });

        var credential = SecretGenerator.CreateToken();
        try
        {
            await _agents.ActivateWithCodeAsync(installation.Id, SecretHasher.Hash(request.ActivationCode),
                request.AgentId, request.MachineFingerprint, SecretHasher.Hash(credential), DateTimeOffset.UtcNow, cancellationToken);
        }
        catch (BusinessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }

        return Ok(new ActivateAgentResponse(request.AgentId, credential));
    }

    [AllowAnonymous]
    [HttpPost("token")]
    public async Task<IActionResult> Token([FromBody] AgentTokenRequest request, CancellationToken cancellationToken)
    {
        if (request is null || request.AgentId == Guid.Empty || string.IsNullOrWhiteSpace(request.Credential))
        {
            return BadRequest(new { error = "TOKEN_REQUEST_INVALID" });
        }

        var agent = await _agents.GetByIdAsync(request.AgentId, cancellationToken);
        if (agent is null ||
            string.IsNullOrWhiteSpace(agent.CredentialHash) ||
            !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(agent.CredentialHash),
                Convert.FromHexString(SecretHasher.Hash(request.Credential))))
        {
            return Unauthorized(new { error = "AGENT_CREDENTIAL_INVALID" });
        }

        // Lisans, kimlik dogrulama yolunda da zorlanir. Aksi halde sona erme yalnizca import ve
        // aktivasyon aninda kontrol edilir; suresi dolmus lisansta zaten aktive edilmis ajanlar
        // 10 dakikalik tokeni sonsuza dek yenileyerek calismaya devam ederdi.
        var status = await _licenses.GetStatusAsync(cancellationToken);
        if (!status.IsValid)
        {
            return Unauthorized(new { error = status.ErrorCode ?? "LICENSE_INVALID" });
        }

        // Agent, lisansli BU kuruluma ait olmalidir — baska bir kurulumdan tasinmis kayitlar
        // (kopyalanmis veritabani) bu kurulumun koltuklarini kullanamaz.
        var installation = await _licenses.GetCurrentInstallationAsync(cancellationToken);
        if (installation is null || agent.LicenseInstallationId != installation.Id)
        {
            return Unauthorized(new { error = "LICENSE_INSTALLATION_MISMATCH" });
        }

        try
        {
            return Ok(new AgentTokenResponse(_tokens.GenerateAccessToken(agent)));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }
}

public sealed record ActivateAgentRequest(Guid AgentId, string InstallationId, string ActivationCode, string MachineFingerprint);
public sealed record ActivateAgentResponse(Guid AgentId, string Credential);
public sealed record AgentTokenRequest(Guid AgentId, string Credential);
public sealed record AgentTokenResponse(string AccessToken);

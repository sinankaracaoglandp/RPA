namespace RPA.WebAPI.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RPA.Domain.Entities;
using RPA.Domain.Exceptions;
using RPA.Infrastructure.Services;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}/einvoice-profiles")]
public sealed class EInvoiceProfilesController : ControllerBase
{
    private readonly EInvoiceProfileService _service;

    public EInvoiceProfilesController(EInvoiceProfileService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EInvoiceProfileDto>>> List(Guid projectId, CancellationToken cancellationToken) =>
        Ok((await _service.ListAsync(projectId, cancellationToken)).Select(EInvoiceProfileDto.From));

    [HttpPost]
    public async Task<ActionResult<EInvoiceProfileDto>> Create(Guid projectId, CreateEInvoiceProfileRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var profile = await _service.CreateAsync(projectId, request.Name, request.Description, cancellationToken);
            return CreatedAtAction(nameof(Get), new { projectId, profileId = profile.Id }, EInvoiceProfileDto.From(profile));
        }
        catch (BusinessException exception) { return BadRequest(exception.Message); }
    }

    [HttpGet("{profileId:guid}")]
    public async Task<ActionResult<EInvoiceProfileDto>> Get(Guid projectId, Guid profileId, CancellationToken cancellationToken)
    {
        try { return Ok(EInvoiceProfileDto.From(await _service.GetAsync(projectId, profileId, cancellationToken))); }
        catch (BusinessException exception) { return NotFound(exception.Message); }
    }

    [HttpPut("{profileId:guid}/draft")]
    public async Task<ActionResult<EInvoiceProfileDto>> SaveDraft(Guid projectId, Guid profileId, SaveEInvoiceProfileDraftRequest request, CancellationToken cancellationToken)
    {
        try { return Ok(EInvoiceProfileDto.From(await _service.SaveDraftAsync(projectId, profileId, request.DefinitionJson, cancellationToken))); }
        catch (BusinessException exception) { return BadRequest(exception.Message); }
    }

    [HttpPost("{profileId:guid}/publish")]
    public async Task<ActionResult<EInvoiceProfileVersionDto>> Publish(Guid projectId, Guid profileId, CancellationToken cancellationToken)
    {
        try { return Ok(EInvoiceProfileVersionDto.From(await _service.PublishAsync(projectId, profileId, UserId(), cancellationToken))); }
        catch (BusinessException exception) { return BadRequest(exception.Message); }
    }

    [HttpGet("{profileId:guid}/versions")]
    public async Task<ActionResult<IReadOnlyList<EInvoiceProfileVersionDto>>> ListVersions(Guid projectId, Guid profileId, CancellationToken cancellationToken)
    {
        try { return Ok((await _service.ListVersionsAsync(projectId, profileId, cancellationToken)).Select(EInvoiceProfileVersionDto.From)); }
        catch (BusinessException exception) { return NotFound(exception.Message); }
    }

    [HttpGet("{profileId:guid}/versions/{version:int}")]
    public async Task<ActionResult<EInvoiceProfileVersionDto>> GetVersion(Guid projectId, Guid profileId, int version, CancellationToken cancellationToken)
    {
        try { return Ok(EInvoiceProfileVersionDto.From(await _service.GetVersionAsync(projectId, profileId, version, cancellationToken))); }
        catch (BusinessException exception) { return NotFound(exception.Message); }
    }

    [HttpDelete("{profileId:guid}")]
    public async Task<IActionResult> Delete(Guid projectId, Guid profileId, CancellationToken cancellationToken)
    {
        try { await _service.DeleteAsync(projectId, profileId, cancellationToken); return NoContent(); }
        catch (BusinessException exception) { return NotFound(exception.Message); }
    }

    private Guid? UserId() => Guid.TryParse(User?.FindFirst("sub")?.Value, out var id) ? id : null;
}

public sealed record CreateEInvoiceProfileRequest(string Name, string? Description);
public sealed record SaveEInvoiceProfileDraftRequest(string DefinitionJson);

public sealed record EInvoiceProfileDto(Guid Id, Guid ProjectId, string Name, string? Description, string DraftDefinitionJson, DateTime CreatedAt, DateTime? UpdatedAt)
{
    public static EInvoiceProfileDto From(EInvoiceProfile value) =>
        new(value.Id, value.ProjectId, value.Name, value.Description, value.DraftDefinitionJson, value.CreatedAt, value.UpdatedAt);
}

public sealed record EInvoiceProfileVersionDto(Guid Id, Guid ProfileId, int Version, string DefinitionJson, string OutputSchemaJson, DateTime PublishedAt, Guid? PublishedBy)
{
    public static EInvoiceProfileVersionDto From(EInvoiceProfileVersion value) =>
        new(value.Id, value.ProfileId, value.Version, value.DefinitionJson, value.OutputSchemaJson, value.PublishedAt, value.PublishedBy);
}

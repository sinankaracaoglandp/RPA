namespace RPA.WebAPI.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RPA.Domain.Entities;
using RPA.Infrastructure.Services;
using BusinessException = RPA.Domain.Exceptions.BusinessException;

/// <summary>Workflow taslak (draft) kaydet/yükle uçları (Paket B).</summary>
[ApiController]
[Route("api/workflows/{workflowId}/draft")]
[Authorize]
public class WorkflowsController : ControllerBase
{
    private readonly WorkflowDesignService _service;

    public WorkflowsController(WorkflowDesignService service) => _service = service;

    /// <summary>Taslak versiyonu (JsonDefinition dahil) döndürür; yoksa boş taslak oluşturur.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<WorkflowDraftDto>> GetDraft(string workflowId, CancellationToken ct)
    {
        if (!Guid.TryParse(workflowId, out var id))
        {
            return BadRequest(new { error = "'workflowId' geçerli bir GUID olmalıdır." });
        }
        try
        {
            return Ok(Map(await _service.GetDraftAsync(id, ct)));
        }
        catch (BusinessException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Taslağı kaydeder; JSON şema v1.0'a karşı doğrulanır (geçersizse 400).</summary>
    [HttpPut]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<WorkflowDraftDto>> SaveDraft(
        string workflowId, [FromBody] SaveDraftRequest request, CancellationToken ct)
    {
        if (!Guid.TryParse(workflowId, out var id))
        {
            return BadRequest(new { error = "'workflowId' geçerli bir GUID olmalıdır." });
        }
        if (request is null || string.IsNullOrWhiteSpace(request.JsonDefinition))
        {
            return BadRequest(new { error = "'jsonDefinition' zorunludur." });
        }
        try
        {
            return Ok(Map(await _service.SaveDraftAsync(id, request.JsonDefinition, ct)));
        }
        catch (BusinessException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    private static WorkflowDraftDto Map(WorkflowVersion v) => new()
    {
        Id = v.Id, WorkflowId = v.WorkflowId, Version = v.Version, JsonDefinition = v.JsonDefinition,
    };
}

public class SaveDraftRequest
{
    public string JsonDefinition { get; set; } = string.Empty;
}

public class WorkflowDraftDto
{
    public Guid Id { get; set; }
    public Guid WorkflowId { get; set; }
    public string Version { get; set; } = string.Empty;
    public string JsonDefinition { get; set; } = string.Empty;
}

namespace RPA.WebAPI.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RPA.Domain.Entities;
using RPA.Infrastructure.Services;
using Environment = RPA.Domain.Entities.Environment;

/// <summary>
/// Ortam (Dev/Test/Prod) yönetimi uç noktaları (WP-6.4, Spec Bölüm 5.5).
/// Ortam yönetimi ekranı ve deployment governance akışının hedeflerini sağlar.
/// </summary>
[ApiController]
[Route("api/environments")]
[Authorize]
public class EnvironmentsController : ControllerBase
{
    private readonly EnvironmentService _service;

    public EnvironmentsController(EnvironmentService service)
    {
        _service = service;
    }

    /// <summary>Tüm ortamları listeler.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<EnvironmentDto>>> List(CancellationToken ct)
    {
        var envs = await _service.ListAsync(ct);
        return Ok(envs.Select(Map).ToList());
    }

    /// <summary>Yeni ortam oluşturur (yalnızca "Approver" rolü).</summary>
    [Authorize(Roles = WorkflowDeploymentService.ApproverRole)]
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<EnvironmentDto>> Create(
        [FromBody] CreateEnvironmentRequest request, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "'name' zorunludur." });
        }

        var created = await _service.CreateAsync(request.Name, request.Description, ct);
        return Ok(Map(created));
    }

    private static EnvironmentDto Map(Environment e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        Description = e.Description,
    };
}

public class CreateEnvironmentRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class EnvironmentDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

namespace RPA.WebAPI.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RPA.Infrastructure.Services;

/// <summary>
/// Component publish/approve governance uç noktaları (Spec Bölüm 9, WP-4.5 — publish/approve
/// akışının ilk uçtan uca doğrulaması: "SAP Login" component'i).
/// </summary>
[ApiController]
[Route("api/components")]
public class ComponentsController : ControllerBase
{
    private readonly ComponentPublishService _publishService;

    public ComponentsController(ComponentPublishService publishService)
    {
        _publishService = publishService;
    }

    /// <summary>
    /// "SAP Login" component'ini kütüphaneye yayına hazırlar (Draft, onay bekliyor).
    /// Yalnızca "Developer" rolü çağırabilir.
    /// </summary>
    [Authorize(Roles = ComponentPublishService.DeveloperRole)]
    [HttpPost("sap-login/publish")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> PublishSapLogin(
        [FromBody] PublishComponentRequest request, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.JsonDefinition))
        {
            return BadRequest(new { error = "'jsonDefinition' zorunludur." });
        }

        var roles = User.Claims
            .Where(c => c.Type == System.Security.Claims.ClaimTypes.Role)
            .Select(c => c.Value)
            .ToList();

        var result = await _publishService.PublishAsync(
            SapLoginComponentIds.ComponentId,
            string.IsNullOrWhiteSpace(request.Version) ? "1.0.0" : request.Version,
            request.JsonDefinition,
            request.InputOutputSchema ?? "{}",
            roles,
            ct);

        return Ok(new
        {
            componentId = result.ComponentId,
            version = result.Version,
            status = result.Status.ToString(),
        });
    }

    /// <summary>
    /// Draft durumundaki "SAP Login" versiyonunu onaylar → Published. Yalnızca "Approver" rolü.
    /// </summary>
    [Authorize(Roles = ComponentPublishService.ApproverRole)]
    [HttpPost("sap-login/{version}/approve")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ApproveSapLogin(string version, CancellationToken ct)
    {
        var roles = User.Claims
            .Where(c => c.Type == System.Security.Claims.ClaimTypes.Role)
            .Select(c => c.Value)
            .ToList();

        var result = await _publishService.ApproveAsync(
            SapLoginComponentIds.ComponentId, version, roles, ct);

        return Ok(new
        {
            componentId = result.ComponentId,
            version = result.Version,
            status = result.Status.ToString(),
        });
    }
}

/// <summary>SAP Login component'inin sabit kimliği (Spec Bölüm 5.3 örneği, WP-4.5).</summary>
public static class SapLoginComponentIds
{
    public static readonly Guid ComponentId = Guid.Parse("00000000-0000-0000-0000-000000000101");
}

public class PublishComponentRequest
{
    public string? Version { get; set; }
    public string JsonDefinition { get; set; } = string.Empty;
    public string? InputOutputSchema { get; set; }
}

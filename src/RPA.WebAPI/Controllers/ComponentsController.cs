namespace RPA.WebAPI.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RPA.Domain.Entities;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Services;

/// <summary>
/// Component publish/approve governance ve kütüphane uç noktaları (Spec Bölüm 9).
/// Faz 4 Task 4.5'te yalnızca "SAP Login" component'i için hardcoded rotalar vardı
/// (bkz. CLAUDE.md "Kontrat Değişikliği — 2026-07-06"); bu controller herhangi bir
/// component için listeleme/detay/publish/approve akışını genelleştirir (Faz 5 Task 5.3
/// Component Library UI bağımlılığı).
/// </summary>
[ApiController]
[Route("api/components")]
[Authorize]
public class ComponentsController : ControllerBase
{
    private readonly ComponentPublishService _publishService;
    private readonly IComponentStore _store;

    public ComponentsController(ComponentPublishService publishService, IComponentStore store)
    {
        _publishService = publishService;
        _store = store;
    }

    /// <summary>Yayınlanmış (Published) tüm component versiyonlarını listeler.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ComponentVersionDto>>> ListComponents(CancellationToken ct)
    {
        var components = await _store.GetPublishedVersionsAsync();
        return Ok(components.Select(Map).ToList());
    }

    /// <summary>Belirtilen component + versiyon kaydını döndürür.</summary>
    [HttpGet("{componentId}/{version}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<ComponentVersionDto> GetComponent(string componentId, string version)
    {
        if (!Guid.TryParse(componentId, out var id))
        {
            return BadRequest(new { error = "'componentId' geçerli bir GUID olmalıdır." });
        }

        var component = _store.GetVersions(componentId)
            .FirstOrDefault(v => string.Equals(v.Version, version, StringComparison.OrdinalIgnoreCase));

        if (component is null)
        {
            return NotFound();
        }

        return Ok(Map(component));
    }

    /// <summary>
    /// Bir component'i kütüphaneye yayına hazırlar (Draft, onay bekliyor).
    /// Yalnızca "Developer" rolü çağırabilir.
    /// </summary>
    [Authorize(Roles = ComponentPublishService.DeveloperRole)]
    [HttpPost("{componentId}/publish")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ComponentVersionDto>> PublishComponent(
        string componentId, [FromBody] PublishComponentRequest request, CancellationToken ct)
    {
        if (!Guid.TryParse(componentId, out var id))
        {
            return BadRequest(new { error = "'componentId' geçerli bir GUID olmalıdır." });
        }
        if (request is null || string.IsNullOrWhiteSpace(request.JsonDefinition))
        {
            return BadRequest(new { error = "'jsonDefinition' zorunludur." });
        }

        var roles = CallerRoles();

        var result = await _publishService.PublishAsync(
            id,
            string.IsNullOrWhiteSpace(request.Version) ? "1.0.0" : request.Version,
            request.JsonDefinition,
            request.InputOutputSchema ?? "{}",
            roles,
            ct);

        result.DisplayName = request.DisplayName;
        result.Description = request.Description;
        result.Author = request.Author;
        result.Category = request.Category;

        return Ok(Map(result));
    }

    /// <summary>
    /// Draft durumundaki bir component versiyonunu onaylar → Published.
    /// Yalnızca "Approver" rolü çağırabilir.
    /// </summary>
    [Authorize(Roles = ComponentPublishService.ApproverRole)]
    [HttpPost("{componentId}/{version}/approve")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ComponentVersionDto>> ApproveComponent(
        string componentId, string version, CancellationToken ct)
    {
        if (!Guid.TryParse(componentId, out var id))
        {
            return BadRequest(new { error = "'componentId' geçerli bir GUID olmalıdır." });
        }

        var roles = CallerRoles();

        var result = await _publishService.ApproveAsync(id, version, roles, ct);

        return Ok(Map(result));
    }

    private List<string> CallerRoles() => User.Claims
        .Where(c => c.Type == System.Security.Claims.ClaimTypes.Role)
        .Select(c => c.Value)
        .ToList();

    private static ComponentVersionDto Map(ComponentVersion c) => new()
    {
        ComponentId = c.ComponentId,
        Version = c.Version,
        Status = c.Status.ToString(),
        DisplayName = c.DisplayName,
        Description = c.Description,
        Author = c.Author,
        Category = c.Category,
        JsonDefinition = c.JsonDefinition,
        InputOutputSchema = c.InputOutputSchema,
    };
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
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Author { get; set; }
    public string? Category { get; set; }
}

public class ComponentVersionDto
{
    public Guid ComponentId { get; set; }
    public string Version { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Author { get; set; }
    public string? Category { get; set; }
    public string JsonDefinition { get; set; } = string.Empty;
    public string InputOutputSchema { get; set; } = string.Empty;
}

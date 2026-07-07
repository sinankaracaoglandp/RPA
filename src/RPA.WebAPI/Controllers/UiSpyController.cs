namespace RPA.WebAPI.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using RPA.Infrastructure.UISpy;
using RPA.WebAPI.Hubs;

/// <summary>
/// UI Spy REST uç noktası (Task 4.4, Spec Bölüm 6). On-demand (istek üzerine) element yayınlama:
/// bir ajan / Studio, tespit ettiği elementi POST eder; controller bunu <see cref="StudioHub"/>
/// üzerinden tüm Studio istemcilerine yayınlar. SignalR hub yolunun (ReceiveDetectedElement) REST
/// alternatifidir.
///
/// Güvenlik: yalnızca kimliği doğrulanmış oturum (attended tasarım akışı) çağırabilir.
/// </summary>
[ApiController]
[Route("api/uispy")]
[Authorize]
public class UiSpyController : ControllerBase
{
    private readonly IHubContext<StudioHub> _studioHub;

    public UiSpyController(IHubContext<StudioHub> studioHub)
    {
        _studioHub = studioHub;
    }

    /// <summary>
    /// Tespit edilen elementi Studio istemcilerine yayınlar (POST /api/uispy/detect).
    /// </summary>
    [HttpPost("detect")]
    [ProducesResponseType(typeof(SpyElementMessage), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Detect([FromBody] SpyElementMessage element, CancellationToken ct)
    {
        if (element is null || string.IsNullOrWhiteSpace(element.ElementId))
        {
            return BadRequest(new { error = "ElementId zorunludur." });
        }

        if (element.SessionId != Guid.Empty)
        {
            if (StudioHub.TryGetSessionOwner(element.SessionId, out var ownerConnectionId))
            {
                await _studioHub.Clients.Client(ownerConnectionId).SendAsync(StudioHub.DetectedElementEvent, element, ct);
            }

            return Ok(element);
        }

        await _studioHub.Clients.All.SendAsync(StudioHub.DetectedElementEvent, element, ct);
        return Ok(element);
    }
}

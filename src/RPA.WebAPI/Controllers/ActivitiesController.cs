namespace RPA.WebAPI.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RPA.Infrastructure.Workflow;

/// <summary>
/// Aktivite kataloğu uç noktaları — Studio Toolbox'ının okuduğu tek referans (Spec Bölüm 5.3).
/// <see cref="ActivityCatalog"/> singleton'ından metadata sunar; frontend
/// <c>ActivityCatalogService</c> (GET /api/activities) bunu tüketir.
/// </summary>
[ApiController]
[Route("api/activities")]
[Authorize]
public class ActivitiesController : ControllerBase
{
    private readonly ActivityCatalog _catalog;

    public ActivitiesController(ActivityCatalog catalog)
    {
        _catalog = catalog;
    }

    /// <summary>Tüm katalog aktivitelerini listeler (Toolbox popülasyonu).</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IEnumerable<object>> List()
    {
        var activities = _catalog.ListAll().Values;
        return Ok(activities);
    }

    /// <summary>Tek bir aktivitenin metadata'sını döndürür; yoksa 404.</summary>
    [HttpGet("{activityId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<object> Get(string activityId)
    {
        var metadata = _catalog.GetActivityMetadata(activityId);
        if (metadata is null)
        {
            return NotFound(new { error = $"Aktivite bulunamadı: {activityId}" });
        }
        return Ok(metadata);
    }
}

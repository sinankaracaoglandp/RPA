namespace RPA.WebAPI.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RPA.Infrastructure.Workflow.Expressions;

/// <summary>
/// İfade fonksiyon kataloğu uç noktası — Studio autocomplete'in okuduğu tek referans.
/// <see cref="FunctionRegistry.Catalog"/> metadata'sını sunar (frontend ExpressionFunctionService tüketir).
/// </summary>
[ApiController]
[Route("api/expression")]
[Authorize]
public class ExpressionController : ControllerBase
{
    /// <summary>Tüm ifade fonksiyonlarını (ad, kategori, imza, açıklama, örnek) listeler.</summary>
    [HttpGet("functions")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<ExpressionFunctionInfo>> GetFunctions() => Ok(FunctionRegistry.Catalog);
}

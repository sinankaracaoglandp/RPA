namespace RPA.WebAPI.Licensing;

using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RPA.Domain.Exceptions;
using RPA.Domain.Interfaces;
using RPA.Domain.Licensing;
using RPA.Infrastructure.Licensing;

[ApiController]
[Route("api/license")]
public sealed class LicenseController : ControllerBase
{
    private readonly ILicenseService _licenses;

    public LicenseController(ILicenseService licenses) => _licenses = licenses;

    [Authorize(Policy = "LicenseAdministrator")]
    [HttpPost("import")]
    public async Task<IActionResult> Import([FromBody] JsonElement document, CancellationToken cancellationToken)
    {
        SignedLicenseDocument signed;
        try
        {
            signed = LicenseDocumentJson.Read(document);
        }
        catch (JsonException)
        {
            // Yanlis dosya secildi / belge bozuk: bu bir istemci hatasidir. Icerigi yankilamayiz.
            return BadRequest(new { error = "LICENSE_DOCUMENT_INVALID" });
        }

        try
        {
            return Ok(await _licenses.ImportAsync(signed, cancellationToken));
        }
        catch (BusinessException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [Authorize(Policy = "LicenseAdministrator")]
    [HttpGet("installation-request")]
    public async Task<ActionResult<InstallationRequestDocument>> ExportInstallationRequest(CancellationToken cancellationToken) =>
        Ok(await _licenses.ExportInstallationRequestAsync(cancellationToken));

    [Authorize(Policy = "LicenseAdministrator")]
    [HttpGet("status")]
    public async Task<ActionResult<LicenseStatus>> Status(CancellationToken cancellationToken) =>
        Ok(await _licenses.GetStatusAsync(cancellationToken));
}

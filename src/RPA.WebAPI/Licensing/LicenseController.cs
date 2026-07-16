namespace RPA.WebAPI.Licensing;

using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RPA.Domain.Exceptions;
using RPA.Domain.Interfaces;
using RPA.Domain.Licensing;

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
        try
        {
            return Ok(await _licenses.ImportAsync(ReadSignedLicense(document), cancellationToken));
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

    private static SignedLicenseDocument ReadSignedLicense(JsonElement document)
    {
        var payload = GetProperty(document, "Payload");
        var features = GetProperty(payload, "Features").EnumerateArray()
            .Select(x => x.GetString()!)
            .ToArray();

        return new SignedLicenseDocument(
            new OfflineLicensePayload(
                GetProperty(payload, "SchemaVersion").GetInt32(),
                GetProperty(payload, "LicenseId").GetString()!,
                GetProperty(payload, "Revision").GetInt32(),
                GetProperty(payload, "CustomerId").GetString()!,
                GetProperty(payload, "InstallationId").GetString()!,
                GetProperty(payload, "InstallationPublicKeyFingerprint").GetString()!,
                GetProperty(payload, "MaxActivatedAgents").GetInt32(),
                GetProperty(payload, "IssuedAt").GetDateTimeOffset(),
                GetProperty(payload, "ExpiresAt").GetDateTimeOffset(),
                features),
            GetProperty(document, "Signature").GetString()!,
            GetProperty(document, "Algorithm").GetString()!);
    }

    private static JsonElement GetProperty(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        throw new JsonException($"Missing required property '{name}'.");
    }
}

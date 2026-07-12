namespace RPA.WebAPI.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RPA.Domain.Interfaces;

/// <summary>
/// Credential Vault yonetimi. Secret degeri yalnizca yazma isteginde alinir;
/// listeleme ve cevaplarda plaintext kesinlikle donmez.
/// </summary>
[ApiController]
[Route("api/credentials")]
[Authorize]
public class CredentialsController : ControllerBase
{
    private readonly ICredentialVault _vault;
    private readonly ILogger<CredentialsController> _logger;

    public CredentialsController(ICredentialVault vault, ILogger<CredentialsController> logger)
    {
        _vault = vault;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<CredentialReferenceDto>>> List(
        [FromQuery] string? tag,
        CancellationToken ct)
    {
        var references = await _vault.ListSecretsAsync(tag);
        return Ok(references.Select(Map).ToList());
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CredentialReferenceDto>> Store(
        [FromBody] StoreCredentialRequest request,
        CancellationToken ct)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.Key) ||
            string.IsNullOrWhiteSpace(request.Secret))
        {
            return BadRequest(new { error = "'key' ve 'secret' zorunludur." });
        }

        var metadata = BuildMetadata(request);
        await _vault.StoreSecretAsync(request.Key.Trim(), new SecureString(request.Secret), metadata);
        _logger.LogInformation("Credential kaydedildi: {Key}, tags: {Tags}",
            request.Key.Trim(), string.Join(",", metadata.Keys));

        return Ok(new CredentialReferenceDto
        {
            Key = request.Key.Trim(),
            Type = metadata.GetValueOrDefault("type"),
            Environment = metadata.GetValueOrDefault("env"),
            Description = metadata.GetValueOrDefault("description"),
            Metadata = metadata,
        });
    }

    [HttpDelete("{*key}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Delete(string key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return BadRequest(new { error = "'key' zorunludur." });
        }

        await _vault.DeleteSecretAsync(Uri.UnescapeDataString(key));
        return NoContent();
    }

    private static Dictionary<string, string> BuildMetadata(StoreCredentialRequest request)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddIfPresent(metadata, "type", request.Type);
        AddIfPresent(metadata, "env", request.Environment);
        AddIfPresent(metadata, "description", request.Description);

        if (request.Metadata is not null)
        {
            foreach (var pair in request.Metadata)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key) &&
                    !string.IsNullOrWhiteSpace(pair.Value) &&
                    !string.Equals(pair.Key, "value", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(pair.Key, "secret", StringComparison.OrdinalIgnoreCase))
                {
                    metadata[pair.Key.Trim()] = pair.Value.Trim();
                }
            }
        }

        return metadata;
    }

    private static void AddIfPresent(Dictionary<string, string> metadata, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            metadata[key] = value.Trim();
        }
    }

    private static CredentialReferenceDto Map(VaultSecretReference reference)
    {
        reference.Metadata.TryGetValue("type", out var type);
        reference.Metadata.TryGetValue("env", out var environment);
        reference.Metadata.TryGetValue("description", out var description);

        return new CredentialReferenceDto
        {
            Key = reference.Key,
            Type = type,
            Environment = environment,
            Description = description,
            Metadata = reference.Metadata,
        };
    }
}

public sealed class StoreCredentialRequest
{
    public string Key { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? Environment { get; set; }
    public string? Description { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}

public sealed class CredentialReferenceDto
{
    public string Key { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? Environment { get; set; }
    public string? Description { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}

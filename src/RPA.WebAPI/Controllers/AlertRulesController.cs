namespace RPA.WebAPI.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RPA.Domain.Entities;
using RPA.Domain.Interfaces;

/// <summary>
/// Alarm kuralları yönetimi (WP-6.3, Spec Bölüm 8.2): listeleme, oluşturma, aktif/pasif değiştirme.
/// Yalnızca yetkili kullanıcılar (Yönetici) erişir.
/// </summary>
[ApiController]
[Route("api/alert-rules")]
[Authorize]
public class AlertRulesController : ControllerBase
{
    private readonly IAlertRuleRepository _repository;

    public AlertRulesController(IAlertRuleRepository repository)
    {
        _repository = repository;
    }

    /// <summary>Tüm alarm kuralları.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<AlertRuleDto>>> List(CancellationToken ct)
    {
        var rules = await _repository.ListAllAsync(ct);
        return Ok(rules.Select(Map).ToList());
    }

    /// <summary>Yeni alarm kuralı oluşturur.</summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AlertRuleDto>> Create(
        [FromBody] CreateAlertRuleRequest request, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Name) ||
            string.IsNullOrWhiteSpace(request.Condition) || string.IsNullOrWhiteSpace(request.Channel))
        {
            return BadRequest(new { error = "'name', 'condition' ve 'channel' zorunludur." });
        }

        var rule = new AlertRule
        {
            Name = request.Name,
            Condition = request.Condition,
            Channel = request.Channel,
            Recipients = request.Recipients ?? string.Empty,
            IsActive = request.IsActive,
        };
        await _repository.AddAsync(rule, ct);
        await _repository.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(List), new { id = rule.Id }, Map(rule));
    }

    /// <summary>Kuralın aktif/pasif durumunu değiştirir.</summary>
    [HttpPatch("{id:guid}/active")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AlertRuleDto>> SetActive(
        Guid id, [FromBody] SetActiveRequest request, CancellationToken ct)
    {
        var rule = await _repository.FindByIdAsync(id, ct);
        if (rule is null)
        {
            return NotFound();
        }

        rule.IsActive = request?.IsActive ?? false;
        await _repository.SaveChangesAsync(ct);
        return Ok(Map(rule));
    }

    private static AlertRuleDto Map(AlertRule a) => new()
    {
        Id = a.Id,
        Name = a.Name,
        Condition = a.Condition,
        Channel = a.Channel,
        Recipients = a.Recipients,
        IsActive = a.IsActive,
    };
}

public class CreateAlertRuleRequest
{
    public string Name { get; set; } = string.Empty;
    public string Condition { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string? Recipients { get; set; }
    public bool IsActive { get; set; } = true;
}

public class SetActiveRequest
{
    public bool IsActive { get; set; }
}

public class AlertRuleDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Condition { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string Recipients { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

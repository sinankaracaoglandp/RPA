namespace RPA.Infrastructure.Services;

using RPA.Domain.Entities;
using RPA.Domain.Interfaces;
using BusinessException = RPA.Domain.Exceptions.BusinessException;
using Environment = RPA.Domain.Entities.Environment;

/// <summary>
/// Ortam (Dev/Test/Prod) yönetimi (WP-6.4, Spec Bölüm 5.5 — ortam bazlı Credential/Asset izolasyonu).
/// Deployment governance akışı bu ortamları hedef alır (Test'e publish, Prod'a approve).
/// </summary>
public sealed class EnvironmentService
{
    /// <summary>Standart ortam adları — sistem ilk açılışında oluşturulur.</summary>
    public static readonly string[] DefaultEnvironments = { "Dev", "Test", "Prod" };

    private readonly IEnvironmentRepository _repository;

    public EnvironmentService(IEnvironmentRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <summary>Tüm ortamları listeler.</summary>
    public Task<IReadOnlyList<Environment>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    /// <summary>
    /// Standart ortamlardan (Dev/Test/Prod) eksik olanları oluşturur. Idempotent —
    /// mevcut olanlar tekrar eklenmez. Oluşturulan ortam sayısını döner.
    /// </summary>
    public async Task<int> EnsureDefaultsAsync(CancellationToken ct = default)
    {
        var created = 0;
        foreach (var name in DefaultEnvironments)
        {
            var existing = await _repository.FindByNameAsync(name, ct).ConfigureAwait(false);
            if (existing is null)
            {
                await _repository.AddAsync(new Environment { Id = Guid.NewGuid(), Name = name }, ct)
                    .ConfigureAwait(false);
                created++;
            }
        }

        if (created > 0)
        {
            await _repository.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return created;
    }

    /// <summary>
    /// Yeni ortam oluşturur. Ad boş olamaz ve mevcut bir ortamla (case-insensitive) çakışamaz.
    /// </summary>
    public async Task<Environment> CreateAsync(string name, string? description, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new BusinessException("Ortam adı boş olamaz.");
        }

        var trimmed = name.Trim();
        var existing = await _repository.FindByNameAsync(trimmed, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            throw new BusinessException($"'{trimmed}' adında bir ortam zaten mevcut.");
        }

        var entity = new Environment
        {
            Id = Guid.NewGuid(),
            Name = trimmed,
            Description = description ?? "",
        };

        await _repository.AddAsync(entity, ct).ConfigureAwait(false);
        await _repository.SaveChangesAsync(ct).ConfigureAwait(false);
        return entity;
    }
}

namespace RPA.Domain.Interfaces;

using RPA.Domain.Entities;

/// <summary>
/// Orchestrator UI (WP-6.1) için JobRun read-side sorgu arayüzü. Agent protokolünden
/// (yürütme) ayrı; yalnızca listeleme/detay/özet döndürür — mutasyon içermez.
/// </summary>
public interface IJobRunQueryRepository
{
    /// <summary>İşler ekranı: filtrelenmiş + sayfalanmış JobRun listesi (StartedAt azalan).</summary>
    Task<JobRunPage> ListAsync(JobRunQuery query, CancellationToken cancellationToken = default);

    /// <summary>İş detayı; bulunamazsa null.</summary>
    Task<JobRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Dashboard özeti: verilen gün (UTC) ve opsiyonel ortam için toplu sayaçlar.</summary>
    Task<DashboardSummary> GetDashboardSummaryAsync(
        DateTime dayUtc, Guid? environmentId, CancellationToken cancellationToken = default);
}

/// <summary>JobRun listeleme filtresi. Null alanlar filtrelenmez.</summary>
public sealed record JobRunQuery(
    string? Status = null,
    Guid? EnvironmentId = null,
    Guid? RobotId = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    int Skip = 0,
    int Take = 50);

/// <summary>Sayfalanmış sonuç: toplam eşleşen kayıt + geçerli sayfa.</summary>
public sealed record JobRunPage(IReadOnlyList<JobRun> Items, int TotalCount);

/// <summary>Dashboard kartları: bir günün iş sayaçları ve başarı oranı.</summary>
public sealed record DashboardSummary(
    int Total,
    int Running,
    int Successful,
    int Failed,
    int BusinessException,
    int Abandoned,
    double SuccessRate);

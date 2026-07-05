namespace RPA.Agent.Jobs;

/// <summary>
/// Ajana iş sağlayan kaynak soyutlaması. Varsayılan implementasyon kuyruk motorunu
/// (IQueueService) sarar; test için sahte kaynak kullanılabilir. İş izolasyonu: her
/// dequeue tek bir kalem döndürür, raporlama kalem kimliğiyle yapılır.
/// </summary>
public interface IAgentJobSource
{
    /// <summary>Sıradaki işi atomik olarak çeker (yoksa null).</summary>
    Task<AgentJob?> DequeueAsync(CancellationToken cancellationToken = default);

    /// <summary>Kalemi başarıyla tamamlandı olarak raporlar.</summary>
    Task ReportSuccessAsync(Guid itemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Kalemi başarısız raporlar. <paramref name="isBusinessException"/> true ise retry edilmez
    /// (Action Center'a düşer); false ise kuyruk motoru retry politikasını uygular.
    /// </summary>
    Task ReportFailureAsync(Guid itemId, string? errorDetail, bool isBusinessException, CancellationToken cancellationToken = default);
}

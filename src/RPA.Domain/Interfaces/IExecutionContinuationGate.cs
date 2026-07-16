namespace RPA.Domain.Interfaces;

/// <summary>
/// Yürütme süreklilik kapısı (Task 6 — offline lisans kirası).
/// Runner, SIRADAKİ node'u başlatmadan önce bu kapıya danışır. Kapı yalnız node SINIRINDA
/// uygulanır: çalışan bir node asla yarıda kesilmez (Spec — "Connectivity and Offline Lease").
/// </summary>
/// <remarks>
/// Ajan süreci bunu bağlantı kirasıyla (15 dk) implemente eder. Ajan-dışı süreçlerde
/// (Studio çalıştırması, testler) kapı kayıtlı değildir → sınır uygulanmaz.
/// </remarks>
public interface IExecutionContinuationGate
{
    /// <summary>
    /// Sonraki node başlayabilir mi? Başlayamıyorsa
    /// <see cref="Exceptions.ExecutionSuspendedException"/> fırlatır.
    /// </summary>
    /// <param name="jobRunId">Çalışan işin kimliği (askıya alma raporunda korunur).</param>
    /// <param name="nodeId">Başlatılmak istenen node kimliği.</param>
    Task EnsureMayStartNodeAsync(Guid jobRunId, string nodeId, CancellationToken cancellationToken = default);
}

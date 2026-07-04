namespace RPA.Domain.Interfaces;

using RPA.Domain.Entities;

/// <summary>
/// Workflow/Component JSON'ını çalıştıran state machine.
/// Spec Bölüm 5.2 — Node graph'ını topological sıraya sokarak yürütür.
/// </summary>
public interface IWorkflowRunner
{
    /// <summary>
    /// Workflow'u çalıştır.
    /// </summary>
    /// <param name="workflowVersion">Workflow JSON'ı ve metadata</param>
    /// <param name="arguments">Giriş değerleri (workflow'un Input argümanları)</param>
    /// <param name="jobRunId">Korelasyon ID (logging için)</param>
    /// <param name="cancellationToken">İptal sinyali</param>
    /// <returns>Çıkış değerleri (workflow'un Output argümanları)</returns>
    /// <exception cref="BusinessException">İş kuralı hlal — Action Center'a düşer</exception>
    /// <exception cref="SystemException">Teknik hata — retry politikası uygulanır</exception>
    Task<WorkflowExecutionResult> ExecuteAsync(
        WorkflowVersion workflowVersion,
        Dictionary<string, object?> arguments,
        Guid jobRunId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Component'i çalıştır (izole scope, versiyon pinleme).
    /// </summary>
    /// <param name="componentVersion">Component JSON'ı</param>
    /// <param name="inputs">Component giriş parametreleri</param>
    /// <param name="jobRunId">Korelasyon ID</param>
    /// <returns>Component çıkış değerleri</returns>
    Task<Dictionary<string, object?>> InvokeComponentAsync(
        ComponentVersion componentVersion,
        Dictionary<string, object?> inputs,
        Guid jobRunId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Daha önce kaydedilmiş bir checkpoint'ten devam eder. Checkpoint node'una kadar
    /// tamamlanmış adımlar tekrar çalıştırılmaz; değişken durumu geri yüklenir ve
    /// yürütme checkpoint'ten sonraki node'dan devam eder (Spec Bölüm 5.2, 2.6).
    /// </summary>
    /// <param name="workflowVersion">Workflow JSON'ı ve metadata</param>
    /// <param name="arguments">Ek/override argümanlar (checkpoint'teki değişkenlerin üzerine yazılır)</param>
    /// <param name="checkpointData">ICheckpointManager.Serialize ile üretilmiş veri (örn. QueueItem.CheckpointData)</param>
    /// <param name="jobRunId">Korelasyon ID (logging için)</param>
    Task<WorkflowExecutionResult> ResumeAsync(
        WorkflowVersion workflowVersion,
        Dictionary<string, object?> arguments,
        string checkpointData,
        Guid jobRunId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Workflow çalıştırma sonucu.
/// </summary>
public class WorkflowExecutionResult
{
    /// <summary>
    /// Başarılı mı?
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Çıkış değerleri (workflow Output argümanları).
    /// </summary>
    public Dictionary<string, object?> Outputs { get; set; } = new();

    /// <summary>
    /// İstisna söz konusu ise.
    /// </summary>
    public Exception? Exception { get; set; }

    /// <summary>
    /// Çalıştırma süresi (ms).
    /// </summary>
    public long DurationMs { get; set; }

    /// <summary>
    /// Yürütme sırasında en az bir checkpoint node'u çalıştıysa, o andaki durumun
    /// serileştirilmiş hali (ICheckpointManager.Serialize). Yoksa null.
    /// Çağıran taraf (Kuyruk motoru) bunu QueueItem.CheckpointData'ya yazar;
    /// başarısız/kesilen çalıştırmalarda ResumeAsync ile devam ettirmek için kullanılır.
    /// </summary>
    public string? CheckpointData { get; set; }
}

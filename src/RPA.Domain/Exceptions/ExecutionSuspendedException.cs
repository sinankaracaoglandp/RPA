namespace RPA.Domain.Exceptions;

/// <summary>
/// Bağlantı kirası dolduğu için yürütme SONRAKİ node'dan önce askıya alındı (Task 6).
/// Sistem seviyesi bir kesintidir (<see cref="SystemException"/> türevi) — iş kuralı ihlali DEĞİL:
/// bağlantı geri geldiğinde iş yeniden yetkilendirilip devam ettirilebilir.
/// </summary>
/// <remarks>
/// İş ve sonraki-node kimliğini korur; böylece bağlantı döndüğünde askıya alma tam olarak
/// nerede olduğuyla raporlanabilir.
/// </remarks>
public sealed class ExecutionSuspendedException : SystemException
{
    public ExecutionSuspendedException(Guid jobRunId, string nextNodeId, string message)
        : base(message)
    {
        JobRunId = jobRunId;
        NextNodeId = nextNodeId;
    }

    /// <summary>Askıya alınan işin kimliği.</summary>
    public Guid JobRunId { get; }

    /// <summary>Başlatılamayan (sıradaki) node kimliği — devam noktası.</summary>
    public string NextNodeId { get; }
}

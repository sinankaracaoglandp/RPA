namespace RPA.Domain.Interfaces;

/// <summary>
/// Uzun akışlarda durum kaydı (checkpoint) — Spec Bölüm 5.2, 2.6:
/// "Checkpoint node durum kaydeder; resume yapılırsa tamamlanmış adımlar atlanır."
/// [MVP: adım bazlı checkpoint-resume — QueueItem.CheckpointData alanında saklanır.]
/// </summary>
public interface ICheckpointManager
{
    /// <summary>Son çalıştırılan checkpoint node'unun kimliği ve o andaki global
    /// değişken durumunu tek bir JSON dizgesine serileştirir.</summary>
    string Serialize(string? lastCheckpointNodeId, IReadOnlyDictionary<string, object?> variables);

    /// <summary>Daha önce <see cref="Serialize"/> ile üretilmiş veriyi geri çözer.
    /// Veri yoksa/boşsa null döner (checkpoint yok → baştan çalıştır).</summary>
    CheckpointState? Deserialize(string? checkpointData);
}

/// <summary>Bir checkpoint anındaki yürütme durumu.</summary>
public sealed class CheckpointState
{
    public CheckpointState(string? lastCheckpointNodeId, IReadOnlyDictionary<string, object?> variables)
    {
        LastCheckpointNodeId = lastCheckpointNodeId;
        Variables = variables;
    }

    /// <summary>Kaydedildiği andaki en son çalıştırılmış checkpoint node ID'si.</summary>
    public string? LastCheckpointNodeId { get; }

    /// <summary>O andaki global değişken anlık görüntüsü.</summary>
    public IReadOnlyDictionary<string, object?> Variables { get; }
}

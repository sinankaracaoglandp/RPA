namespace RPA.Agent.Configuration;

using RPA.Domain.Enums;

/// <summary>
/// Robot ajanı yapılandırması (Spec Bölüm 5.6, 9). appsettings.json'daki "Agent" bölümünden okunur.
/// Orchestrator adresi, robot kimliği (makine adı/mod/etiket), işlenecek kuyruk ve döngü aralıkları.
/// </summary>
public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    /// <summary>Orchestrator (WebAPI) taban adresi. Örn. https://orchestrator:5001.</summary>
    public string OrchestratorUrl { get; set; } = "";

    /// <summary>Robot makine adı. Boşsa <see cref="Environment.MachineName"/> kullanılır.</summary>
    public string MachineName { get; set; } = "";

    /// <summary>Robot çalışma modu (Attended tray gösterir, Unattended servis).</summary>
    public RobotMode Mode { get; set; } = RobotMode.Unattended;

    /// <summary>Robot etiketleri (ör. "sap,finance") — kuyruk yönlendirme için.</summary>
    public string Tags { get; set; } = "";

    /// <summary>Eşzamanlı iş kapasitesi.</summary>
    public int Capacity { get; set; } = 1;

    /// <summary>İşlenecek kuyruğun kimliği.</summary>
    public Guid QueueId { get; set; }

    /// <summary>Kuyruk yoklama aralığı. Varsayılan 5 saniye (Spec Bölüm 9).</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Heartbeat aralığı. Varsayılan 30 saniye; offline eşiği 5 dk (Spec Bölüm 9).</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Etkin makine adını döndürür (yapılandırma boşsa ortamdan alır).</summary>
    public string EffectiveMachineName
        => string.IsNullOrWhiteSpace(MachineName) ? Environment.MachineName : MachineName;
}

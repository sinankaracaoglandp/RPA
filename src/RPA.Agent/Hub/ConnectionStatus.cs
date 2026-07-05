namespace RPA.Agent.Hub;

/// <summary>Tray'de gösterilen SignalR bağlantı durumu (Spec Bölüm 9 — otomatik yeniden bağlanma).</summary>
public enum ConnectionStatus
{
    Online,
    Reconnecting,
    Offline,
}

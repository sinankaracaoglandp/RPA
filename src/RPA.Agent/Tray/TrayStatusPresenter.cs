namespace RPA.Agent.Tray;

using System.Globalization;
using RPA.Agent.Hub;
using RPA.Agent.State;

/// <summary>
/// Tray ikonu için durum metnini/ipucunu biçimlendirir (attended mod, Spec Bölüm 5.6). UI çatısından
/// (WinForms NotifyIcon) bağımsız — böylece Windows dışında da test edilebilir. Gerçek NotifyIcon
/// bu sunucudan metni okuyup gösterir; duraklat/devam/durdur komutlarını <see cref="IAgentState"/>'e iletir.
/// </summary>
public sealed class TrayStatusPresenter
{
    private readonly IAgentState _state;
    private ConnectionStatus _connectionStatus = ConnectionStatus.Online;

    public TrayStatusPresenter(IAgentState state)
        => _state = state ?? throw new ArgumentNullException(nameof(state));

    /// <summary>SignalR bağlantı durumunu günceller (tooltip/menüye yansır). Coordinator olayına bağlanır.</summary>
    public void UpdateConnectionStatus(ConnectionStatus status) => _connectionStatus = status;

    /// <summary>Tray tooltip metni (ikonun üzerine gelince görünür).</summary>
    public string GetTooltip()
    {
        var robot = _state.RobotId is { } id ? id.ToString()[..8] : "kayıtsız";
        var activity = DescribeActivity(_state.Activity);
        var jobs = string.Format(
            CultureInfo.InvariantCulture,
            "Tamamlanan: {0}  Başarısız: {1}",
            _state.CompletedJobCount, _state.FailedJobCount);
        var connection = DescribeConnection(_connectionStatus);
        return $"RPA Robot [{robot}]\n{activity}\n{jobs}\n{connection}";
    }

    /// <summary>Bağlantı durumuna karşılık gelen kısa etiket (Çevrimiçi/Yeniden bağlanılıyor/Çevrimdışı).</summary>
    public string GetConnectionLabel() => DescribeConnection(_connectionStatus);

    /// <summary>İkon renk/duruma karşılık gelen kısa etiket.</summary>
    public string GetStatusLabel() => DescribeActivity(_state.Activity);

    /// <summary>Duraklat/devam menü öğesinin başlığı.</summary>
    public string GetPauseResumeCaption() => _state.IsPaused ? "Devam Et" : "Duraklat";

    /// <summary>Tray'den duraklat/devam geçişini uygular.</summary>
    public void TogglePause() => _state.SetPaused(!_state.IsPaused);

    /// <summary>"İşi Durdur" menü öğesinin başlığı.</summary>
    public string GetStopJobCaption() => Localization.AgentStrings.Get("Tray.StopJob", Localization.AgentLanguage.Turkish);

    /// <summary>"İş Listesini Aç" menü öğesinin başlığı.</summary>
    public string GetOpenJobListCaption() => Localization.AgentStrings.Get("Tray.OpenJobList", Localization.AgentLanguage.Turkish);

    /// <summary>"Ajanı Kapat" menü öğesinin başlığı.</summary>
    public string GetExitCaption() => Localization.AgentStrings.Get("Tray.ExitAgent", Localization.AgentLanguage.Turkish);

    /// <summary>Şu anda çalışan işin durdurulmasının anlamlı olup olmadığı (iş yoksa menü devre dışı bırakılabilir).</summary>
    public bool CanStopJob() => _state.CurrentJobId is not null;

    private static string DescribeConnection(ConnectionStatus status) => status switch
    {
        ConnectionStatus.Online => Localization.AgentStrings.Get("Tray.Online", Localization.AgentLanguage.Turkish),
        ConnectionStatus.Reconnecting => Localization.AgentStrings.Get("Tray.Reconnecting", Localization.AgentLanguage.Turkish),
        ConnectionStatus.Offline => Localization.AgentStrings.Get("Tray.Offline", Localization.AgentLanguage.Turkish),
        _ => status.ToString(),
    };

    private static string DescribeActivity(AgentActivity activity) => activity switch
    {
        AgentActivity.Starting => "Başlatılıyor…",
        AgentActivity.Registering => "Kaydolunuyor…",
        AgentActivity.Idle => "Boşta (iş bekleniyor)",
        AgentActivity.Running => "Çalışıyor…",
        AgentActivity.Paused => "Duraklatıldı",
        AgentActivity.Stopped => "Durduruldu",
        _ => activity.ToString(),
    };
}

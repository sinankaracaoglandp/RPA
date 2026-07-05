namespace RPA.Agent.Tray;

using System.Globalization;
using RPA.Agent.State;

/// <summary>
/// Tray ikonu için durum metnini/ipucunu biçimlendirir (attended mod, Spec Bölüm 5.6). UI çatısından
/// (WinForms NotifyIcon) bağımsız — böylece Windows dışında da test edilebilir. Gerçek NotifyIcon
/// bu sunucudan metni okuyup gösterir; duraklat/devam komutlarını <see cref="IAgentState"/>'e iletir.
/// </summary>
public sealed class TrayStatusPresenter
{
    private readonly IAgentState _state;

    public TrayStatusPresenter(IAgentState state)
        => _state = state ?? throw new ArgumentNullException(nameof(state));

    /// <summary>Tray tooltip metni (ikonun üzerine gelince görünür).</summary>
    public string GetTooltip()
    {
        var robot = _state.RobotId is { } id ? id.ToString()[..8] : "kayıtsız";
        var activity = DescribeActivity(_state.Activity);
        var jobs = string.Format(
            CultureInfo.InvariantCulture,
            "Tamamlanan: {0}  Başarısız: {1}",
            _state.CompletedJobCount, _state.FailedJobCount);
        return $"RPA Robot [{robot}]\n{activity}\n{jobs}";
    }

    /// <summary>İkon renk/duruma karşılık gelen kısa etiket.</summary>
    public string GetStatusLabel() => DescribeActivity(_state.Activity);

    /// <summary>Duraklat/devam menü öğesinin başlığı.</summary>
    public string GetPauseResumeCaption() => _state.IsPaused ? "Devam Et" : "Duraklat";

    /// <summary>Tray'den duraklat/devam geçişini uygular.</summary>
    public void TogglePause() => _state.SetPaused(!_state.IsPaused);

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

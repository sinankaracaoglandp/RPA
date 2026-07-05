namespace RPA.Agent.Session;

using RPA.Domain.Interfaces;

/// <summary>
/// Windows oturum bilgilerini (WTS API) sorgular. Test edilebilirlik için ayrı arayüz.
/// </summary>
public interface ISessionInfoProvider
{
    /// <summary>Etkin konsol oturumunu döndürür.</summary>
    SessionInfo GetActiveSession();

    /// <summary>Makinedeki tüm oturumları listeler.</summary>
    IReadOnlyList<SessionInfo> ListSessions();
}

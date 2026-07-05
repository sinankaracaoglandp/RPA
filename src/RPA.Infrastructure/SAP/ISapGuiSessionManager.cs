namespace RPA.Infrastructure.SAP;

/// <summary>
/// SAP GUI Scripting oturumlarının yaşam döngüsü yöneticisi — COM logon/logoff.
/// Bir robot süreci içinde birden fazla eşzamanlı GUI oturumu tutulabilir.
/// Gerçek COM interop (sapfewse.ocx / GuiApplication) ayrı entegrasyon testinde doğrulanır;
/// bu arayüz üzerinden <see cref="SapGuiChannel"/> mock'lanabilir.
/// </summary>
public interface ISapGuiSessionManager
{
    /// <summary>
    /// SAP sistemine oturum aç (COM logon) ve hazır bir oturum döndür.
    /// </summary>
    Task<ISapGuiSession> LogonAsync(
        string systemId,
        string client,
        string userId,
        string password,
        string language = "EN",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verilen oturumdan çık (COM logoff) ve kaynakları serbest bırak.
    /// </summary>
    Task LogoffAsync(ISapGuiSession session, CancellationToken cancellationToken = default);

    /// <summary>Şu an açık olan oturumlar.</summary>
    IReadOnlyCollection<ISapGuiSession> ActiveSessions { get; }
}

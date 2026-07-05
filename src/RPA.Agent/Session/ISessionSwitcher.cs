namespace RPA.Agent.Session;

/// <summary>
/// Oturum değiştirme işlemini (tscon.exe) soyutlar. Test edilebilirlik için ayrı arayüz.
/// </summary>
public interface ISessionSwitcher
{
    /// <summary>
    /// Belirtilen oturumu konsola taşır: <c>tscon.exe &lt;sessionId&gt; /dest:console</c>.
    /// </summary>
    Task SwitchToConsoleAsync(int sessionId, CancellationToken cancellationToken = default);
}

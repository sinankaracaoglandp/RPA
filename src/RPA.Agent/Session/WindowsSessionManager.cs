namespace RPA.Agent.Session;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;

/// <summary>
/// <see cref="ISessionManager"/> Windows implementasyonu (Spec Bölüm 9).
///
/// - Attended: hiçbir otomatik işlem yapılmaz (kullanıcı oturumu, manuel).
/// - Unattended: AutoLogon kurulur (yalnızca <see cref="SessionManagerOptions.AllowAutoLogon"/>
///   true iken — güvenlik gereği dev/test). Parola Vault'tan okunur, registry'ye yalnızca
///   yazma anında iletilir; loglara asla düşmez.
/// - Rdp: oturum kopmuşsa (Disconnected/LoggedOff) tscon ile konsola geri bağlanır.
///
/// Registry/tscon/WTS erişimi arayüzlerle soyutlanmıştır; bu sınıf saf orkestrasyon içerir.
/// </summary>
public sealed class WindowsSessionManager : ISessionManager
{
    private readonly ISessionInfoProvider _sessionInfo;
    private readonly IAutoLogonRegistry _autoLogonRegistry;
    private readonly ISessionSwitcher _switcher;
    private readonly SessionCredentialProvider _credentialProvider;
    private readonly SessionManagerOptions _options;
    private readonly ILogger<WindowsSessionManager> _logger;

    public WindowsSessionManager(
        ISessionInfoProvider sessionInfo,
        IAutoLogonRegistry autoLogonRegistry,
        ISessionSwitcher switcher,
        SessionCredentialProvider credentialProvider,
        IOptions<SessionManagerOptions> options,
        ILogger<WindowsSessionManager> logger)
    {
        _sessionInfo = sessionInfo ?? throw new ArgumentNullException(nameof(sessionInfo));
        _autoLogonRegistry = autoLogonRegistry ?? throw new ArgumentNullException(nameof(autoLogonRegistry));
        _switcher = switcher ?? throw new ArgumentNullException(nameof(switcher));
        _credentialProvider = credentialProvider ?? throw new ArgumentNullException(nameof(credentialProvider));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task EnsureSessionAsync(SessionMode mode, CancellationToken cancellationToken = default)
    {
        switch (mode)
        {
            case SessionMode.Attended:
                // Attended: kullanıcı oturumu, otomatik geçiş yok.
                _logger.LogInformation("Attended mod: otomatik oturum işlemi yapılmıyor.");
                return;

            case SessionMode.Unattended:
                await ConfigureAutoLogonAsync(cancellationToken).ConfigureAwait(false);
                return;

            case SessionMode.Rdp:
                await ReconnectIfNeededAsync(cancellationToken).ConfigureAwait(false);
                return;

            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Bilinmeyen oturum modu.");
        }
    }

    public Task<SessionInfo> GetActiveSessionAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_sessionInfo.GetActiveSession());

    public async Task SwitchToConsoleAsync(int sessionId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Oturum {SessionId} konsola taşınıyor (tscon).", sessionId);
        await _switcher.SwitchToConsoleAsync(sessionId, cancellationToken).ConfigureAwait(false);
    }

    public async Task ReconnectIfNeededAsync(CancellationToken cancellationToken = default)
    {
        var active = _sessionInfo.GetActiveSession();
        if (active.State == SessionState.Active)
        {
            _logger.LogDebug("Oturum {SessionId} etkin, yeniden bağlanmaya gerek yok.", active.SessionId);
            return;
        }

        // Kopmuş/kapanmış oturum: kullanıcı oturumunu bul ve konsola taşı.
        var candidate = FindReconnectCandidate();
        if (candidate is null)
        {
            _logger.LogWarning(
                "Yeniden bağlanılacak oturum bulunamadı (etkin durum: {State}).", active.State);
            return;
        }

        _logger.LogInformation(
            "Oturum {SessionId} durumu {State}; konsola yeniden bağlanılıyor.",
            candidate.SessionId, candidate.State);
        await _switcher.SwitchToConsoleAsync(candidate.SessionId, cancellationToken).ConfigureAwait(false);
    }

    private SessionInfo? FindReconnectCandidate()
    {
        SessionInfo? best = null;
        foreach (var session in _sessionInfo.ListSessions())
        {
            if (session.State is SessionState.Disconnected or SessionState.Locked)
            {
                // Kopmuş bir kullanıcı oturumu tercih edilir.
                if (session.UserName is not null)
                {
                    return session;
                }

                best ??= session;
            }
        }

        return best;
    }

    private async Task ConfigureAutoLogonAsync(CancellationToken cancellationToken)
    {
        if (!_options.AllowAutoLogon)
        {
            throw new InvalidOperationException(
                "AutoLogon devre dışı. Güvenlik gereği yalnızca development/test ortamında " +
                "(Agent:Session:AllowAutoLogon=true) etkinleştirilebilir.");
        }

        var credential = await _credentialProvider
            .GetAutoLogonCredentialAsync(cancellationToken).ConfigureAwait(false);

        // Parola yalnızca registry yazma anında çözülür; log'a düşmez.
        var password = credential.RevealPassword();
        _autoLogonRegistry.Configure(credential.UserName, credential.Domain, password);

        _logger.LogInformation(
            "Unattended AutoLogon kuruldu: kullanıcı {UserName}, alan {Domain}.",
            credential.UserName, credential.Domain ?? "(yerel)");
    }
}

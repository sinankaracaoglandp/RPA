namespace RPA.Domain.Interfaces;

using RPA.Domain.Enums;

/// <summary>
/// Robotun her zaman etkin bir Windows oturumuna sahip olmasını sağlar (Spec Bölüm 9).
/// Unattended modda başlangıçta AutoLogon kurar; oturum kopmalarında tscon ile
/// konsola geri bağlanır. Kimlik bilgileri asla plaintext saklanmaz — Vault'tan okunur.
/// </summary>
public interface ISessionManager
{
    /// <summary>
    /// Verilen mod için etkin oturumu garanti eder. Attended modda hiçbir otomatik
    /// işlem yapılmaz (kullanıcı oturumu). Unattended modda AutoLogon kurulur
    /// (yalnızca yapılandırmada izin verildiyse). Rdp modda oturum kopmuşsa yeniden bağlanır.
    /// </summary>
    Task EnsureSessionAsync(SessionMode mode, CancellationToken cancellationToken = default);

    /// <summary>Şu an etkin (konsol) oturum bilgisini döndürür.</summary>
    Task<SessionInfo> GetActiveSessionAsync(CancellationToken cancellationToken = default);

    /// <summary>Belirtilen oturumu konsola taşır (tscon &lt;id&gt; /dest:console).</summary>
    Task SwitchToConsoleAsync(int sessionId, CancellationToken cancellationToken = default);

    /// <summary>Oturum kopmuş/kapanmışsa konsol oturumuna yeniden bağlanır.</summary>
    Task ReconnectIfNeededAsync(CancellationToken cancellationToken = default);
}

/// <summary>Bir Windows oturumunun anlık durumu.</summary>
/// <param name="SessionId">Windows oturum kimliği.</param>
/// <param name="State">Oturum durumu.</param>
/// <param name="UserName">Oturum sahibi kullanıcı (bilinmiyorsa null).</param>
public sealed record SessionInfo(int SessionId, SessionState State, string? UserName);

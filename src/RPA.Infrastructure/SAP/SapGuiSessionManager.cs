namespace RPA.Infrastructure.SAP;

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using BusinessException = RPA.Domain.Exceptions.BusinessException;
using SystemException = RPA.Domain.Exceptions.SystemException;

/// <summary>
/// SAP GUI Scripting oturum yöneticisi (Task 4.1). COM logon/logoff yaşam döngüsünü yönetir.
///
/// NOT: Gerçek COM interop (sapfewse.ocx üzerinden <c>GuiApplication.OpenConnection</c> ve
/// <c>GuiConnection.Children</c>) yalnızca üzerinde SAP GUI kurulu Windows makinede çalışır ve
/// ayrı bir entegrasyon testinde (real DEP) doğrulanır. Burada oturum, deterministik bir
/// <see cref="StubSapGuiSession"/> ile temsil edilir; böylece kanal ve aktivite mantığı
/// SAP GUI kurulumu olmadan birim testlerle doğrulanabilir.
/// </summary>
public sealed class SapGuiSessionManager : ISapGuiSessionManager
{
    private readonly ILogger<SapGuiSessionManager> _logger;
    private readonly ConcurrentDictionary<string, ISapGuiSession> _sessions = new();

    public SapGuiSessionManager(ILogger<SapGuiSessionManager> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyCollection<ISapGuiSession> ActiveSessions => _sessions.Values.ToList().AsReadOnly();

    public Task<ISapGuiSession> LogonAsync(
        string systemId,
        string client,
        string userId,
        string password,
        string language = "EN",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(systemId))
        {
            throw new BusinessException("SAP System ID (systemId) boş olamaz.");
        }

        if (string.IsNullOrWhiteSpace(client))
        {
            throw new BusinessException("SAP Client boş olamaz.");
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new BusinessException("SAP kullanıcı adı (userId) boş olamaz.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // Gerçek impl: GuiApplication.OpenConnectionByConnectionString(...); session = conn.Children(0);
            //             session.findById("wnd[0]/usr/txtRSYST-BNAME").text = userId; ... sendVKey 0;
            var session = new StubSapGuiSession(systemId, client, userId, language);
            _sessions[session.Id] = session;

            _logger.LogInformation(
                "SAP GUI oturumu açıldı: Sistem {SystemId}, Client {Client}, Kullanıcı {UserId}, Oturum {SessionId}. Parola: [MASKED]",
                systemId, client, userId, session.Id);

            return Task.FromResult<ISapGuiSession>(session);
        }
        catch (BusinessException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SAP GUI oturumu açılamadı: Sistem {SystemId}, Client {Client}.", systemId, client);
            throw new SystemException($"SAP GUI oturumu açılamadı ({systemId}/{client}): {ex.Message}", ex);
        }
    }

    public Task LogoffAsync(ISapGuiSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        try
        {
            session.Close();
            _sessions.TryRemove(session.Id, out _);
            _logger.LogInformation("SAP GUI oturumu kapatıldı: Oturum {SessionId}.", session.Id);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SAP GUI oturumu kapatılırken hata: Oturum {SessionId}.", session.Id);
            throw new SystemException($"SAP GUI oturumu kapatılamadı ({session.Id}): {ex.Message}", ex);
        }
    }
}

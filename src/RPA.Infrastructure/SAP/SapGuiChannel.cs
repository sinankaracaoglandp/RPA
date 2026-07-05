namespace RPA.Infrastructure.SAP;

using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using RPA.Domain.Interfaces;
using BusinessException = RPA.Domain.Exceptions.BusinessException;
using SystemException = RPA.Domain.Exceptions.SystemException;

/// <summary>
/// SAP GUI Scripting kanalı (fallback) — <see cref="ISapGuiChannel"/> implementasyonu (Task 4.1).
/// Spec Bölüm 6: programatik arayüzü olmayan ekranlar için canlı SAP GUI oturumu üzerinden
/// UI otomasyonu. Oturum yaşam döngüsü <see cref="ISapGuiSessionManager"/> tarafından yönetilir.
///
/// Bu sınıf tek bir aktif oturumu sarmalar; <see cref="ConnectAsync"/> ile açılır,
/// <see cref="DisconnectAsync"/> ile kapanır. Bir kanal örneği bir iş akışı (JobRun) boyunca
/// yaşar; eşzamanlı çağrı için tasarlanmamıştır.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SapGuiChannel : ISapGuiChannel
{
    private readonly ISapGuiSessionManager _sessionManager;
    private readonly ILogger<SapGuiChannel> _logger;
    private ISapGuiSession? _session;

    public SapGuiChannel(ISapGuiSessionManager sessionManager, ILogger<SapGuiChannel> logger)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ConnectAsync(string systemId, string client, string userId, string password, string language = "EN")
    {
        if (_session is { IsConnected: true })
        {
            throw new BusinessException("SAP GUI kanalı zaten bağlı. Önce DisconnectAsync çağırın.");
        }

        _session = await _sessionManager.LogonAsync(systemId, client, userId, password, language);
        _logger.LogInformation("SAP GUI kanalı bağlandı: {SystemId}/{Client} ({SessionId}).", systemId, client, _session.Id);
    }

    public Task ExecuteTransactionAsync(string transactionCode)
    {
        if (string.IsNullOrWhiteSpace(transactionCode))
        {
            throw new BusinessException("Transaction kodu boş olamaz.");
        }

        return GuardedAsync(nameof(ExecuteTransactionAsync), transactionCode,
            s => s.StartTransactionAsync(NormalizeTransaction(transactionCode)));
    }

    public Task ClickAsync(string elementId)
    {
        var id = RequireElementId(elementId);
        return GuardedAsync(nameof(ClickAsync), id, s => s.ClickAsync(id));
    }

    public Task SetTextAsync(string elementId, string text)
    {
        var id = RequireElementId(elementId);
        return GuardedAsync(nameof(SetTextAsync), id, s => s.SetTextAsync(id, text ?? string.Empty));
    }

    public async Task<string> GetTextAsync(string elementId)
    {
        var id = RequireElementId(elementId);
        return await GuardedAsync(nameof(GetTextAsync), id, s => s.GetTextAsync(id));
    }

    public Task SelectTabAsync(string elementId)
    {
        var id = RequireElementId(elementId);
        return GuardedAsync(nameof(SelectTabAsync), id, s => s.SelectTabAsync(id));
    }

    public async Task<List<Dictionary<string, object?>>> ReadGridAsync(string gridId)
    {
        var id = RequireElementId(gridId);
        return await GuardedAsync(nameof(ReadGridAsync), id, s => s.ReadGridAsync(id));
    }

    public async Task<byte[]> CaptureScreenAsync()
    {
        return await GuardedAsync(nameof(CaptureScreenAsync), "-", s => s.CaptureScreenAsync());
    }

    public async Task DisconnectAsync()
    {
        if (_session is null)
        {
            return;
        }

        var session = _session;
        _session = null;
        await _sessionManager.LogoffAsync(session);
        _logger.LogInformation("SAP GUI kanalı bağlantısı kapatıldı: {SessionId}.", session.Id);
    }

    public Task<bool> IsHealthyAsync() => Task.FromResult(_session is { IsConnected: true });

    // ---- yardımcılar ----

    private ISapGuiSession RequireConnected()
    {
        if (_session is null || !_session.IsConnected)
        {
            throw new SystemException("SAP GUI kanalı bağlı değil. Önce ConnectAsync çağırın.");
        }

        return _session;
    }

    private static string RequireElementId(string elementId)
    {
        if (string.IsNullOrWhiteSpace(elementId))
        {
            throw new BusinessException("Element ID boş olamaz.");
        }

        return elementId;
    }

    private static string NormalizeTransaction(string transactionCode)
    {
        var code = transactionCode.Trim();
        return code.StartsWith('/') ? code : "/n" + code;
    }

    private async Task GuardedAsync(string operation, string target, Func<ISapGuiSession, Task> action)
    {
        var session = RequireConnected();
        try
        {
            _logger.LogInformation("SAP GUI {Operation}: {Target} ({SessionId}).", operation, target, session.Id);
            await action(session);
        }
        catch (Exception ex) when (ex is not BusinessException and not SystemException)
        {
            throw new SystemException($"SAP GUI {operation} başarısız ({target}): {ex.Message}", ex);
        }
    }

    private async Task<T> GuardedAsync<T>(string operation, string target, Func<ISapGuiSession, Task<T>> action)
    {
        var session = RequireConnected();
        try
        {
            _logger.LogInformation("SAP GUI {Operation}: {Target} ({SessionId}).", operation, target, session.Id);
            return await action(session);
        }
        catch (Exception ex) when (ex is not BusinessException and not SystemException)
        {
            throw new SystemException($"SAP GUI {operation} başarısız ({target}): {ex.Message}", ex);
        }
    }
}

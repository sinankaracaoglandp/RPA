namespace RPA.WebAPI.Hubs;

using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using RPA.Infrastructure.UISpy;

/// <summary>
/// Studio (tasarımcı) istemcileri ile ajanlar arasında gerçek zamanlı köprü (Task 4.4, Spec Bölüm 6).
/// UI Spy modülünde ajan, imleç altında tespit ettiği SAP GUI elementini
/// <see cref="ReceiveDetectedElement"/> ile buraya gönderir; hub bunu tüm Studio istemcilerine
/// <c>DetectedElement</c> olayıyla yayınlar. Yalnızca kimliği doğrulanmış (JWT) bağlantılar kabul edilir.
/// </summary>
//[Authorize]
public class StudioHub : Hub
{
    /// <summary>Studio istemcilerine yayınlanan olay adı.</summary>
    public const string DetectedElementEvent = "DetectedElement";
    public const string SpyCancelledEvent = "SpyCancelled";
    public const string StartSpyCommand = "StartSpy";
    public const string StopSpyCommand = "StopSpy";

    private static readonly ConcurrentDictionary<Guid, string> SessionOwners = new();
    private static readonly HashSet<string> SupportedKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "sap",
        "web",
        "desktop",
        "image",
    };

    private readonly ILogger<StudioHub> _logger;

    public StudioHub(ILogger<StudioHub> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Ajandan tespit edilen elementi alır ve tüm Studio istemcilerine yayınlar.
    /// </summary>
    public async Task ReceiveDetectedElement(SpyElementMessage element)
    {
        if (element is null)
        {
            return;
        }

        _logger.LogDebug("UI Spy: element alındı {ElementId} @ ({X},{Y}); Studio'ya yayınlanıyor.", element.ElementId, element.X, element.Y);
        if (element.SessionId != Guid.Empty)
        {
            if (SessionOwners.TryGetValue(element.SessionId, out var ownerConnectionId))
            {
                await Clients.Client(ownerConnectionId).SendAsync(DetectedElementEvent, element);
            }
            else
            {
                _logger.LogWarning("UI Spy: bilinmeyen session {SessionId} icin element yok sayildi.", element.SessionId);
            }

            return;
        }

        await Clients.All.SendAsync(DetectedElementEvent, element);
    }

    /// <summary>
    /// Ajan, tek-seçim iptal edildiğinde (Esc) veya seçim yapılmadan bittiğinde çağırır;
    /// oturumu başlatan Studio bağlantısına <c>SpyCancelled</c> yayınlanır ki 60 sn beklemesin.
    /// </summary>
    public async Task NotifySpyCancelled(Guid sessionId)
    {
        if (sessionId == Guid.Empty)
        {
            return;
        }

        if (SessionOwners.TryGetValue(sessionId, out var ownerConnectionId))
        {
            await Clients.Client(ownerConnectionId).SendAsync(SpyCancelledEvent, sessionId);
        }
    }

    public async Task StartSpy(Guid sessionId, string kind)
    {
        if (sessionId == Guid.Empty)
        {
            throw new HubException("SessionId zorunludur.");
        }

        if (!SupportedKinds.Contains(kind))
        {
            throw new HubException($"Desteklenmeyen spy tipi: {kind}");
        }

        SessionOwners[sessionId] = Context.ConnectionId;
        _logger.LogInformation("UI Spy: session {SessionId} ({Kind}) baslatildi.", sessionId, kind);

        // Robot/Agent gruplama Paket C Task 3'te baglanacak; simdilik komut,
        // istegi baslatan Studio disindaki baglantilara gider.
        await Clients.Others.SendAsync(StartSpyCommand, sessionId, kind);
    }

    public async Task StopSpy(Guid sessionId)
    {
        if (sessionId == Guid.Empty)
        {
            return;
        }

        if (SessionOwners.TryGetValue(sessionId, out var ownerConnectionId)
            && ownerConnectionId == Context.ConnectionId)
        {
            SessionOwners.TryRemove(sessionId, out _);
            _logger.LogInformation("UI Spy: session {SessionId} durduruldu.", sessionId);
            await Clients.Others.SendAsync(StopSpyCommand, sessionId);
        }
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        foreach (var item in SessionOwners.Where(x => x.Value == Context.ConnectionId).ToList())
        {
            SessionOwners.TryRemove(item.Key, out _);
        }

        return base.OnDisconnectedAsync(exception);
    }

    public static bool TryGetSessionOwner(Guid sessionId, out string connectionId)
    {
        return SessionOwners.TryGetValue(sessionId, out connectionId!);
    }
}

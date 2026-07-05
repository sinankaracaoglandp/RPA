namespace RPA.WebAPI.Hubs;

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
[Authorize]
public class StudioHub : Hub
{
    /// <summary>Studio istemcilerine yayınlanan olay adı.</summary>
    public const string DetectedElementEvent = "DetectedElement";

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
        await Clients.All.SendAsync(DetectedElementEvent, element);
    }
}

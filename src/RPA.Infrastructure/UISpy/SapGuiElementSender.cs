namespace RPA.Infrastructure.UISpy;

using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using RPA.Domain.ValueObjects;

/// <summary>
/// Tespit edilen elementi Studio'ya taşıyan alt seviye ulaşım (transport) soyutlaması. Somut
/// implementasyon ajan tarafında bir SignalR <c>HubConnection</c> ile StudioHub'ın
/// <c>ReceiveDetectedElement</c> metodunu çağırır. Bu soyutlama sayesinde Infrastructure katmanı
/// SignalR istemcisine doğrudan bağlı değildir ve gönderici birim testlerde mock'lanabilir.
/// </summary>
public interface ISpyElementTransport
{
    /// <summary>Element yükünü Studio'ya (StudioHub.ReceiveDetectedElement) iletir.</summary>
    Task SendAsync(SpyElementMessage message, CancellationToken cancellationToken = default);
}

/// <summary>
/// UI Spy SignalR mesaj sözleşmesi — tespit edilen elementin Studio'ya taşınan düz (flat) gösterimi.
/// </summary>
public sealed record SpyElementMessage
{
    public required string ElementId { get; init; }
    public string? Type { get; init; }
    public string? Text { get; init; }
    public bool Enabled { get; init; }
    public bool Changeable { get; init; }
    public int X { get; init; }
    public int Y { get; init; }

    /// <summary>Bir <see cref="SapGuiElement"/>'ten mesaj oluşturur.</summary>
    public static SpyElementMessage From(SapGuiElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return new SpyElementMessage
        {
            ElementId = element.Id,
            Type = element.Type,
            Text = element.Text,
            Enabled = element.Enabled,
            Changeable = element.Changeable,
            X = element.X,
            Y = element.Y,
        };
    }
}

/// <summary>
/// Tespit edilen SAP GUI elementini Studio'ya (SignalR köprüsü) gönderen bileşen (Spec Bölüm 6).
/// Elementi <see cref="SpyElementMessage"/>'e biçimlendirir ve <see cref="ISpyElementTransport"/>
/// üzerinden StudioHub'a iletir. Windows-only (tespit yalnızca Windows'ta üretilir).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SapGuiElementSender
{
    private readonly ISpyElementTransport _transport;
    private readonly ILogger<SapGuiElementSender> _logger;

    public SapGuiElementSender(ISpyElementTransport transport, ILogger<SapGuiElementSender> logger)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Tespit edilen elementi biçimlendirip Studio'ya gönderir.</summary>
    public async Task SendAsync(SapGuiElement element, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(element);

        var message = SpyElementMessage.From(element);
        await _transport.SendAsync(message, cancellationToken);
        _logger.LogDebug("UI Spy: element Studio'ya gönderildi {ElementId} @ ({X},{Y}).", message.ElementId, message.X, message.Y);
    }
}

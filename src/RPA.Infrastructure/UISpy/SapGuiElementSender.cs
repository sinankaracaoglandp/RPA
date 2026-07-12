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

    /// <summary>
    /// Tek-seçim iptal/boş bittiğinde Studio'ya bildirir (StudioHub.NotifySpyCancelled),
    /// böylece Studio 60 sn beklemeden picker'ı kapatır.
    /// </summary>
    Task NotifyCancelledAsync(Guid sessionId, CancellationToken cancellationToken = default);
}

/// <summary>
/// UI Spy SignalR mesaj sözleşmesi — tespit edilen elementin Studio'ya taşınan düz (flat) gösterimi.
/// </summary>
public sealed record SpyElementMessage
{
    public Guid SessionId { get; init; }
    public string Kind { get; init; } = "sap";
    public required string ElementId { get; init; }
    public string? Type { get; init; }
    public string? Text { get; init; }
    public bool Enabled { get; init; }
    public bool Changeable { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public string? Selector { get; init; }
    public string? TagName { get; init; }
    public string? InnerTextPreview { get; init; }
    public string? PageUrl { get; init; }
    public string? AutomationId { get; init; }
    public string? ControlType { get; init; }
    public string? Name { get; init; }
    public string? UiaPath { get; init; }
    public string? ProcessName { get; init; }
    public string? ImageBase64 { get; init; }
    public string? Region { get; init; }

    /// <summary>Bir <see cref="SapGuiElement"/>'ten mesaj oluşturur.</summary>
    public static SpyElementMessage From(SapGuiElement element) => From(element, Guid.Empty);

    public static SpyElementMessage From(SapGuiElement element, Guid sessionId)
    {
        ArgumentNullException.ThrowIfNull(element);
        return new SpyElementMessage
        {
            SessionId = sessionId,
            Kind = "sap",
            ElementId = element.Id,
            Type = element.Type,
            Text = element.Text,
            Enabled = element.Enabled,
            Changeable = element.Changeable,
            X = element.X,
            Y = element.Y,
        };
    }

    /// <summary>Bir <see cref="WebUiElement"/>'ten web (DOM) mesajı oluşturur.</summary>
    public static SpyElementMessage FromWeb(WebUiElement element, Guid sessionId)
    {
        ArgumentNullException.ThrowIfNull(element);
        return new SpyElementMessage
        {
            SessionId = sessionId,
            Kind = "web",
            ElementId = element.Selector,
            Selector = element.Selector,
            TagName = element.TagName,
            InnerTextPreview = element.InnerTextPreview,
            PageUrl = element.PageUrl,
            Type = element.TagName,
            Text = element.InnerTextPreview,
            Enabled = true,
            Changeable = true,
        };
    }

    /// <summary>Bir <see cref="DesktopUiElement"/>'ten masaüstü (UIA) mesajı oluşturur.</summary>
    public static SpyElementMessage FromDesktop(DesktopUiElement element, Guid sessionId)
    {
        ArgumentNullException.ThrowIfNull(element);
        return new SpyElementMessage
        {
            SessionId = sessionId,
            Kind = "desktop",
            ElementId = element.UiaPath,
            Selector = element.UiaPath,
            UiaPath = element.UiaPath,
            AutomationId = element.AutomationId,
            ControlType = element.ControlType,
            Name = element.Name,
            ProcessName = element.ProcessName,
            Type = element.ControlType,
            Text = element.Name,
            Enabled = true,
            Changeable = true,
            X = element.X,
            Y = element.Y,
        };
    }

    /// <summary>🎯 image picker'dan gelen bölge/görüntü seçiminden mesaj oluşturur.</summary>
    public static SpyElementMessage FromImage(string? imageBase64, string? regionJson, Guid sessionId)
    {
        return new SpyElementMessage
        {
            SessionId = sessionId,
            Kind = "image",
            ElementId = "image",
            ImageBase64 = imageBase64,
            Region = regionJson,
            Selector = imageBase64 ?? regionJson,
            Enabled = true,
            Changeable = true,
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

namespace RPA.Agent.UISpy;

using System.Runtime.Versioning;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RPA.Agent.Configuration;
using RPA.Domain.ValueObjects;
using RPA.Infrastructure.UISpy;

/// <summary>
/// UI Spy orkestrasyon servisi (Task 4.4, Spec Bölüm 6). İmleç altındaki SAP GUI elementini
/// <see cref="SapGuiElementDetector"/> ile tespit eder ve <see cref="SapGuiElementSender"/> ile
/// Studio'ya (SignalR köprüsü) iletir. Aynı elementin tekrar tekrar gönderilmesini önlemek için
/// son gönderilen element ID'sini hatırlar (dedup).
///
/// Güvenlik: yalnızca attended modda + yetkili kullanıcı oturumunda etkinleştirilir
/// (bkz. <see cref="UiSpyHostedService"/> kayıt koşulu). Windows-only.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SapGuiSpyService
{
    private readonly SapGuiElementDetector _detector;
    private readonly SapGuiElementSender _sender;
    private readonly ILogger<SapGuiSpyService> _logger;
    private string? _lastElementId;

    public SapGuiSpyService(
        SapGuiElementDetector detector,
        SapGuiElementSender sender,
        ILogger<SapGuiSpyService> logger)
    {
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// İmleç altındaki elementi tespit eder; SAP elementi bulunursa ve bir öncekinden farklıysa
    /// Studio'ya gönderir. Tespit edilen elementi (veya null) döner.
    /// </summary>
    public async Task<SapGuiElement?> DetectAndSendAsync(CancellationToken cancellationToken = default)
    {
        var element = _detector.DetectElementUnderCursor();
        if (element is null)
        {
            _lastElementId = null;
            return null;
        }

        // Aynı element üzerinde beklerken tekrar tekrar göndermeyi önle.
        if (string.Equals(element.Id, _lastElementId, StringComparison.Ordinal))
        {
            return element;
        }

        _lastElementId = element.Id;
        try
        {
            await _sender.SendAsync(element, cancellationToken);
        }
        catch (Exception ex)
        {
            // Gönderim hatası spy döngüsünü durdurmaz — bir sonraki turda yeniden denenir.
            _lastElementId = null;
            _logger.LogWarning(ex, "UI Spy: element Studio'ya gönderilemedi {ElementId}.", element.Id);
        }

        return element;
    }
}

/// <summary>
/// <see cref="ISpyElementTransport"/>'un SignalR implementasyonu — StudioHub'ın
/// <c>ReceiveDetectedElement</c> metodunu çağırır. Bağlantı tembel (lazy) açılır ve otomatik
/// yeniden bağlanır. Ajan tarafında yaşar; orkestratörün <c>/hubs/studio</c> uç noktasına bağlanır.
/// </summary>
public sealed class SignalRSpyElementTransport : ISpyElementTransport, IAsyncDisposable
{
    private readonly HubConnection _connection;
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private bool _started;

    public SignalRSpyElementTransport(
        IOptions<AgentOptions> options,
        RPA.Agent.Authentication.IAgentAccessTokenProvider tokenProvider)
    {
        var url = options?.Value?.OrchestratorUrl ?? throw new ArgumentNullException(nameof(options));
        ArgumentNullException.ThrowIfNull(tokenProvider);
        _connection = new HubConnectionBuilder()
            .WithUrl(
                $"{url.TrimEnd('/')}/hubs/studio",
                o => o.AccessTokenProvider = async () => await tokenProvider.GetTokenAsync(CancellationToken.None))
            .WithAutomaticReconnect()
            .Build();
    }

    public async Task SendAsync(SpyElementMessage message, CancellationToken cancellationToken = default)
    {
        await EnsureStartedAsync(cancellationToken);
        await _connection.InvokeAsync("ReceiveDetectedElement", message, cancellationToken);
    }

    public async Task NotifyCancelledAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        await EnsureStartedAsync(cancellationToken);
        await _connection.InvokeAsync("NotifySpyCancelled", sessionId, cancellationToken);
    }

    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (_started)
        {
            return;
        }

        await _startGate.WaitAsync(cancellationToken);
        try
        {
            if (!_started)
            {
                await _connection.StartAsync(cancellationToken);
                _started = true;
            }
        }
        finally
        {
            _startGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _startGate.Dispose();
        await _connection.DisposeAsync();
    }
}

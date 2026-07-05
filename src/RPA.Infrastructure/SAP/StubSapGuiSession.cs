namespace RPA.Infrastructure.SAP;

using System.Collections.Concurrent;
using System.Text;

/// <summary>
/// SAP GUI kurulumu olmayan ortamlarda deterministik davranan yer tutucu (stub) oturum.
/// Gerçek COM oturumunun (<c>GuiSession</c>) yerini alır; alan değerlerini bellekte tutar,
/// böylece kanal/aktivite mantığı birim testlerle uçtan uca doğrulanabilir.
/// Gerçek COM implementasyonu ayrı entegrasyon testinde (real DEP) yer alır.
/// </summary>
internal sealed class StubSapGuiSession : ISapGuiSession
{
    private readonly ConcurrentDictionary<string, string> _fields = new();
    private bool _connected = true;

    public StubSapGuiSession(string systemId, string client, string userId, string language)
    {
        SystemId = systemId;
        Client = client;
        UserId = userId;
        Language = language;
        Id = $"{systemId}/{client}/{Guid.NewGuid():N}"[..24];
    }

    public string Id { get; }
    public string SystemId { get; }
    public string Client { get; }
    public string UserId { get; }
    public string Language { get; }
    public bool IsConnected => _connected;

    /// <summary>Son çalıştırılan transaction (test doğrulaması için).</summary>
    public string? CurrentTransaction { get; private set; }

    /// <summary>Son tıklanan element (test doğrulaması için).</summary>
    public string? LastClickedElement { get; private set; }

    /// <summary>Son seçilen tab (test doğrulaması için).</summary>
    public string? LastSelectedTab { get; private set; }

    private void EnsureConnected()
    {
        if (!_connected)
        {
            throw new InvalidOperationException("SAP GUI oturumu kapalı.");
        }
    }

    public Task StartTransactionAsync(string transactionCode)
    {
        EnsureConnected();
        CurrentTransaction = transactionCode;
        return Task.CompletedTask;
    }

    public Task SetTextAsync(string elementId, string text)
    {
        EnsureConnected();
        _fields[elementId] = text;
        return Task.CompletedTask;
    }

    public Task<string> GetTextAsync(string elementId)
    {
        EnsureConnected();
        return Task.FromResult(_fields.TryGetValue(elementId, out var v) ? v : string.Empty);
    }

    public Task ClickAsync(string elementId)
    {
        EnsureConnected();
        LastClickedElement = elementId;
        return Task.CompletedTask;
    }

    public Task SelectTabAsync(string elementId)
    {
        EnsureConnected();
        LastSelectedTab = elementId;
        return Task.CompletedTask;
    }

    public Task<List<Dictionary<string, object?>>> ReadGridAsync(string gridId)
    {
        EnsureConnected();
        return Task.FromResult(new List<Dictionary<string, object?>>());
    }

    public Task<byte[]> CaptureScreenAsync()
    {
        EnsureConnected();
        // Yer tutucu: gerçek impl GuiFrameWindow.HardCopy ile PNG üretir.
        return Task.FromResult(Encoding.UTF8.GetBytes($"SAP-GUI-SCREENSHOT:{Id}:{DateTime.UtcNow:O}"));
    }

    public void Close() => _connected = false;
}

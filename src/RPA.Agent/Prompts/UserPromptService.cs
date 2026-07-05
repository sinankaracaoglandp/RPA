namespace RPA.Agent.Prompts;

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

/// <summary>
/// <see cref="IUserPromptChannel"/> implementasyonu: bekleyen istekleri korelasyon (istek) ID'sine
/// göre tutar; UI (modal pencere) <see cref="PromptRaised"/> olayına abone olup kullanıcı cevabını
/// <see cref="Submit"/> ile geri bildirir. 5 dakika içinde cevap gelmezse istek zaman aşımına uğrar
/// ve <see cref="RequestAsync"/> null döner (Spec Bölüm 9 — UserPrompt node).
/// </summary>
public sealed class UserPromptService : IUserPromptChannel
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<UserPromptResponse?>> _pending = new();
    private readonly ILogger<UserPromptService> _logger;

    public UserPromptService(ILogger<UserPromptService> logger)
        => _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>UI'nin abone olup modal göstermesi için tetiklenen olay.</summary>
    public event Action<UserPromptRequest>? PromptRaised;

    public async Task<UserPromptResponse?> RequestAsync(UserPromptRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tcs = new TaskCompletionSource<UserPromptResponse?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(request.Id, tcs))
            throw new InvalidOperationException($"İstek zaten bekliyor: {request.Id}");

        _logger.LogInformation(
            "UserPrompt isteği oluşturuldu {RequestId} — Başlık: {Title}, Süre: {Timeout}",
            request.Id, request.Title, request.Timeout);

        using var timeoutCts = new CancellationTokenSource(request.Timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        await using var registration = linkedCts.Token.Register(() => tcs.TrySetResult(null));

        try
        {
            PromptRaised?.Invoke(request);
            var response = await tcs.Task;
            if (response is null)
                _logger.LogWarning("UserPrompt isteği zaman aşımına uğradı {RequestId}", request.Id);
            else
                _logger.LogInformation("UserPrompt isteği cevaplandı {RequestId}", request.Id);
            return response;
        }
        finally
        {
            _pending.TryRemove(request.Id, out _);
        }
    }

    /// <summary>UI'nin kullanıcı cevabını bildirmesi için çağrılır. Bilinmeyen/zamanı geçmiş istek için false döner.</summary>
    public bool Submit(UserPromptResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (_pending.TryGetValue(response.RequestId, out var tcs))
            return tcs.TrySetResult(response);
        return false;
    }
}

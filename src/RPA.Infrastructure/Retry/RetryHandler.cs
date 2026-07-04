namespace RPA.Infrastructure.Retry;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using BusinessException = RPA.Domain.Exceptions.BusinessException;

/// <summary>
/// Retry yürütücü (Spec Bölüm 5.2, 6). Bir işlemi <see cref="RetryPolicy"/> uyarınca çalıştırır:
/// SystemException (ve sınıflandırılmamış teknik hata) → gecikmeli yeniden çalıştırma;
/// BusinessException → retry yok, çağırana yayılır (Action Center'a düşer).
/// </summary>
public sealed class RetryHandler
{
    private readonly ExceptionClassifier _classifier;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly ILogger _logger;

    /// <param name="classifier">İstisna sınıflandırıcı.</param>
    /// <param name="delay">Gecikme fonksiyonu (test için enjekte edilebilir; varsayılan <see cref="Task.Delay(TimeSpan, CancellationToken)"/>).</param>
    /// <param name="logger">Opsiyonel logger.</param>
    public RetryHandler(
        ExceptionClassifier classifier,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        ILogger<RetryHandler>? logger = null)
    {
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        _delay = delay ?? ((d, ct) => Task.Delay(d, ct));
        _logger = logger ?? NullLogger<RetryHandler>.Instance;
    }

    /// <summary>Değer döndüren işlemi retry politikasıyla çalıştırır.</summary>
    /// <returns>İşlemin başarılı sonucu.</returns>
    /// <exception cref="BusinessException">İş kuralı ihlali — retry edilmez, doğrudan yayılır.</exception>
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        RetryPolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(policy);

        var attempt = 0;
        while (true)
        {
            attempt++;
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await action(cancellationToken).ConfigureAwait(false);
            }
            catch (BusinessException bex)
            {
                // İş kuralı — retry yok, Action Center'a yayılır.
                _logger.LogWarning(
                    bex, "İş kuralı istisnası (deneme {Attempt}) — retry uygulanmaz.", attempt);
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (_classifier.IsRetryable(ex))
            {
                if (!policy.ShouldRetry(attempt))
                {
                    _logger.LogError(
                        ex, "Sistem istisnası — {Attempt} deneme sonunda tükendi.", attempt);
                    throw;
                }

                var wait = policy.GetDelay(attempt);
                _logger.LogWarning(
                    ex, "Sistem istisnası (deneme {Attempt}/{Max}) — {Delay} ms sonra yeniden denenecek.",
                    attempt, policy.MaxAttempts, wait.TotalMilliseconds);
                await _delay(wait, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Değer döndürmeyen işlemi retry politikasıyla çalıştırır.</summary>
    public Task ExecuteAsync(
        Func<CancellationToken, Task> action,
        RetryPolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return ExecuteAsync<object?>(async ct =>
        {
            await action(ct).ConfigureAwait(false);
            return null;
        }, policy, cancellationToken);
    }
}

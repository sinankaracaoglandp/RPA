namespace RPA.Infrastructure.Retry;

using RPA.Domain.Entities;

/// <summary>
/// Retry politikası — SystemException'lar için deneme sayısı ve gecikme hesabı
/// (Spec Bölüm 5.2, 6). Kuyruk bazında yapılandırılır (<see cref="Queue.MaxRetries"/>,
/// <see cref="Queue.RetryBackoffPolicy"/>).
/// </summary>
public sealed class RetryPolicy
{
    /// <summary>Toplam deneme sayısı (ilk çalıştırma dahil). En az 1.</summary>
    public int MaxAttempts { get; }

    /// <summary>Gecikme büyüme stratejisi.</summary>
    public BackoffStrategy Backoff { get; }

    /// <summary>İlk retry gecikmesi.</summary>
    public TimeSpan InitialDelay { get; }

    /// <summary>Gecikme üst sınırı (backoff bunu aşamaz).</summary>
    public TimeSpan MaxDelay { get; }

    /// <summary>Üstel backoff çarpanı (varsayılan 2.0).</summary>
    public double Multiplier { get; }

    public RetryPolicy(
        int maxAttempts,
        BackoffStrategy backoff = BackoffStrategy.Exponential,
        TimeSpan? initialDelay = null,
        TimeSpan? maxDelay = null,
        double multiplier = 2.0)
    {
        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxAttempts), maxAttempts, "MaxAttempts en az 1 olmalıdır.");
        }
        if (multiplier <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(multiplier), multiplier, "Çarpan pozitif olmalıdır.");
        }

        MaxAttempts = maxAttempts;
        Backoff = backoff;
        InitialDelay = initialDelay ?? TimeSpan.FromSeconds(1);
        MaxDelay = maxDelay ?? TimeSpan.FromMinutes(5);
        Multiplier = multiplier;

        if (InitialDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialDelay), InitialDelay, "InitialDelay negatif olamaz.");
        }
        if (MaxDelay < InitialDelay)
        {
            MaxDelay = InitialDelay;
        }
    }

    /// <summary>
    /// Kuyruk yapılandırmasından politika üretir. <see cref="Queue.MaxRetries"/> retry
    /// sayısıdır; toplam deneme = MaxRetries + 1 (ilk çalıştırma + retry'lar).
    /// </summary>
    public static RetryPolicy FromQueue(Queue queue)
    {
        ArgumentNullException.ThrowIfNull(queue);
        var backoff = ParseBackoff(queue.RetryBackoffPolicy);
        var maxAttempts = Math.Max(1, queue.MaxRetries + 1);
        return new RetryPolicy(maxAttempts, backoff);
    }

    /// <summary>Backoff politika string'ini enum'a çevirir (bilinmeyen → Exponential).</summary>
    public static BackoffStrategy ParseBackoff(string? policy)
        => Enum.TryParse<BackoffStrategy>(policy, ignoreCase: true, out var parsed)
            ? parsed
            : BackoffStrategy.Exponential;

    /// <summary>Verilen retry denemesi sayısı doğrultusunda başka deneme yapılabilir mi?</summary>
    /// <param name="attemptsMade">Şu ana kadar yapılan deneme sayısı (1-tabanlı).</param>
    public bool ShouldRetry(int attemptsMade) => attemptsMade < MaxAttempts;

    /// <summary>
    /// Başarısız <paramref name="attemptNumber"/>. denemeden (1-tabanlı) sonra beklenecek gecikme.
    /// Exponential: InitialDelay * Multiplier^(attemptNumber-1); Linear: InitialDelay * attemptNumber.
    /// Sonuç <see cref="MaxDelay"/> ile sınırlıdır.
    /// </summary>
    public TimeSpan GetDelay(int attemptNumber)
    {
        if (attemptNumber < 1)
        {
            return TimeSpan.Zero;
        }

        double ms = Backoff switch
        {
            BackoffStrategy.Linear => InitialDelay.TotalMilliseconds * attemptNumber,
            BackoffStrategy.Exponential =>
                InitialDelay.TotalMilliseconds * Math.Pow(Multiplier, attemptNumber - 1),
            _ => InitialDelay.TotalMilliseconds,
        };

        if (double.IsInfinity(ms) || ms > MaxDelay.TotalMilliseconds)
        {
            return MaxDelay;
        }
        return TimeSpan.FromMilliseconds(ms);
    }
}

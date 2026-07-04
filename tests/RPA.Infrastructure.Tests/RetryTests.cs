namespace RPA.Infrastructure.Tests;

using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Retry;
using BusinessException = RPA.Domain.Exceptions.BusinessException;
using SystemException = RPA.Domain.Exceptions.SystemException;

/// <summary>
/// Exception sınıflandırma + Retry politikası testleri (Task 2.3.1). Spec Bölüm 5.2, 6.
/// TDD: SystemException retry, BusinessException skip, üstel backoff, metadata kural değerlendirme.
/// </summary>
public class RetryTests
{
    // Test için gerçek gecikme yapmayan, çağrılan gecikmeleri kaydeden yardımcı.
    private sealed class DelayRecorder
    {
        public List<TimeSpan> Delays { get; } = new();
        public Func<TimeSpan, CancellationToken, Task> Func => (d, _) =>
        {
            Delays.Add(d);
            return Task.CompletedTask;
        };
    }

    // ---------------------------------------------------------------- RetryPolicy

    [Fact]
    public void GetDelay_Exponential_DoublesEachAttempt()
    {
        var policy = new RetryPolicy(
            maxAttempts: 5,
            backoff: BackoffStrategy.Exponential,
            initialDelay: TimeSpan.FromSeconds(1),
            maxDelay: TimeSpan.FromMinutes(10),
            multiplier: 2.0);

        Assert.Equal(TimeSpan.FromSeconds(1), policy.GetDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(2), policy.GetDelay(2));
        Assert.Equal(TimeSpan.FromSeconds(4), policy.GetDelay(3));
        Assert.Equal(TimeSpan.FromSeconds(8), policy.GetDelay(4));
    }

    [Fact]
    public void GetDelay_Linear_GrowsLinearly()
    {
        var policy = new RetryPolicy(
            maxAttempts: 5,
            backoff: BackoffStrategy.Linear,
            initialDelay: TimeSpan.FromSeconds(2),
            maxDelay: TimeSpan.FromMinutes(10));

        Assert.Equal(TimeSpan.FromSeconds(2), policy.GetDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(4), policy.GetDelay(2));
        Assert.Equal(TimeSpan.FromSeconds(6), policy.GetDelay(3));
    }

    [Fact]
    public void GetDelay_ExponentialCappedAtMaxDelay()
    {
        var policy = new RetryPolicy(
            maxAttempts: 20,
            backoff: BackoffStrategy.Exponential,
            initialDelay: TimeSpan.FromSeconds(1),
            maxDelay: TimeSpan.FromSeconds(10));

        Assert.Equal(TimeSpan.FromSeconds(10), policy.GetDelay(10)); // 2^9 = 512s → capped
    }

    [Fact]
    public void FromQueue_MapsRetriesAndBackoff()
    {
        var queue = new Queue { MaxRetries = 3, RetryBackoffPolicy = "exponential" };
        var policy = RetryPolicy.FromQueue(queue);

        // 3 retry + 1 ilk çalıştırma = 4 toplam deneme.
        Assert.Equal(4, policy.MaxAttempts);
        Assert.Equal(BackoffStrategy.Exponential, policy.Backoff);
    }

    [Fact]
    public void Constructor_InvalidMaxAttempts_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RetryPolicy(0));
    }

    // ---------------------------------------------------------------- ExceptionClassifier

    [Fact]
    public void Classify_SystemException_IsSystemAndRetryable()
    {
        var c = new ExceptionClassifier();
        var ex = new SystemException("RFC_COMMUNICATION_FAILURE");
        Assert.Equal(ExceptionType.System, c.Classify(ex));
        Assert.True(c.IsRetryable(ex));
    }

    [Fact]
    public void Classify_BusinessException_IsBusinessAndNotRetryable()
    {
        var c = new ExceptionClassifier();
        var ex = new BusinessException("Malzeme zaten mevcut");
        Assert.Equal(ExceptionType.Business, c.Classify(ex));
        Assert.False(c.IsRetryable(ex));
    }

    [Fact]
    public void Classify_UnknownException_TreatedAsSystem()
    {
        var c = new ExceptionClassifier();
        Assert.Equal(ExceptionType.System, c.Classify(new InvalidOperationException("boom")));
        Assert.True(c.IsRetryable(new TimeoutException()));
    }

    [Fact]
    public void Evaluate_SapReturnTypeE_ClassifiedBusiness()
    {
        var c = new ExceptionClassifier();
        var rule = new ExceptionClassificationRule
        {
            Condition = "ReturnType == 'E'",
            Classification = ExceptionType.Business,
        };
        var facts = new Dictionary<string, object?> { ["ReturnType"] = "E" };

        Assert.Equal(ExceptionType.Business, c.Evaluate(rule, facts));
    }

    [Fact]
    public void Evaluate_HttpStatus500_ClassifiedSystem()
    {
        var c = new ExceptionClassifier();
        var rule = new ExceptionClassificationRule
        {
            Condition = "StatusCode >= 500",
            Classification = ExceptionType.System,
        };

        Assert.Equal(ExceptionType.System,
            c.Evaluate(rule, new Dictionary<string, object?> { ["StatusCode"] = 503 }));
        // 404 koşulu sağlamaz → null (eşleşme yok).
        Assert.Null(
            c.Evaluate(rule, new Dictionary<string, object?> { ["StatusCode"] = 404 }));
    }

    [Fact]
    public void Evaluate_NullRule_ReturnsNull()
    {
        var c = new ExceptionClassifier();
        Assert.Null(c.Evaluate(null, new Dictionary<string, object?>()));
    }

    // ---------------------------------------------------------------- RetryHandler

    [Fact]
    public async Task Execute_SystemException_RetriesThreeTimesThenSucceeds()
    {
        var recorder = new DelayRecorder();
        var handler = new RetryHandler(new ExceptionClassifier(), recorder.Func);
        var policy = new RetryPolicy(
            maxAttempts: 4, // 1 ilk + 3 retry
            backoff: BackoffStrategy.Exponential,
            initialDelay: TimeSpan.FromMilliseconds(10));

        var attempts = 0;
        var result = await handler.ExecuteAsync(_ =>
        {
            attempts++;
            if (attempts < 4)
            {
                throw new SystemException($"geçici hata {attempts}");
            }
            return Task.FromResult("ok");
        }, policy);

        Assert.Equal("ok", result);
        Assert.Equal(4, attempts);        // ilk + 3 retry
        Assert.Equal(3, recorder.Delays.Count); // 3 gecikme
        // Üstel backoff sırası doğrulanır.
        Assert.Equal(TimeSpan.FromMilliseconds(10), recorder.Delays[0]);
        Assert.Equal(TimeSpan.FromMilliseconds(20), recorder.Delays[1]);
        Assert.Equal(TimeSpan.FromMilliseconds(40), recorder.Delays[2]);
    }

    [Fact]
    public async Task Execute_SystemExceptionAlwaysFails_ExhaustsAndThrows()
    {
        var recorder = new DelayRecorder();
        var handler = new RetryHandler(new ExceptionClassifier(), recorder.Func);
        var policy = new RetryPolicy(maxAttempts: 3, initialDelay: TimeSpan.FromMilliseconds(1));

        var attempts = 0;
        var ex = await Assert.ThrowsAsync<SystemException>(() =>
            handler.ExecuteAsync<string>(_ =>
            {
                attempts++;
                throw new SystemException("kalıcı hata");
            }, policy));

        Assert.Equal(3, attempts);              // maxAttempts kadar denendi
        Assert.Equal(2, recorder.Delays.Count); // deneme başına 1 gecikme, son denemeden sonra bekleme yok
        Assert.Equal("kalıcı hata", ex.Message);
    }

    [Fact]
    public async Task Execute_BusinessException_SkipsRetryAndPropagates()
    {
        var recorder = new DelayRecorder();
        var handler = new RetryHandler(new ExceptionClassifier(), recorder.Func);
        var policy = new RetryPolicy(maxAttempts: 5, initialDelay: TimeSpan.FromMilliseconds(1));

        var attempts = 0;
        await Assert.ThrowsAsync<BusinessException>(() =>
            handler.ExecuteAsync<string>(_ =>
            {
                attempts++;
                throw new BusinessException("iş kuralı ihlali");
            }, policy));

        Assert.Equal(1, attempts);          // retry yok
        Assert.Empty(recorder.Delays);      // gecikme yok
    }

    [Fact]
    public async Task Execute_SucceedsFirstTry_NoDelay()
    {
        var recorder = new DelayRecorder();
        var handler = new RetryHandler(new ExceptionClassifier(), recorder.Func);
        var policy = new RetryPolicy(maxAttempts: 3);

        var result = await handler.ExecuteAsync(_ => Task.FromResult(42), policy);

        Assert.Equal(42, result);
        Assert.Empty(recorder.Delays);
    }

    [Fact]
    public async Task Execute_Cancellation_Propagates()
    {
        var handler = new RetryHandler(new ExceptionClassifier(), (_, _) => Task.CompletedTask);
        var policy = new RetryPolicy(maxAttempts: 3);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            handler.ExecuteAsync(_ => Task.FromResult(1), policy, cts.Token));
    }
}

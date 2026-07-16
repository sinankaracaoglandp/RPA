namespace RPA.Agent.Connectivity;

using Microsoft.Extensions.Logging;
using RPA.Domain.Exceptions;
using RPA.Domain.Interfaces;

/// <summary>
/// Bağlantı kirası (Task 6): son BAŞARILI sunucu doğrulamasından itibaren en fazla 15 dakikalık
/// offline aralığa izin verir. Kira, JWT ömründen bağımsızdır — token 10 dk, kira 15 dk.
/// </summary>
/// <remarks>
/// Kopma (<see cref="MarkDisconnected"/>) kirayı ANINDA geçersizleştirmez: çalışan node normal
/// tamamlanma sınırına ulaşabilmelidir. Yalnız süre dolduğunda sonraki node engellenir.
/// Zaman <see cref="TimeProvider"/> üzerinden okunur → testler sahte saatle sürer, gerçek bekleme yok.
/// </remarks>
public sealed class ConnectivityLease
{
    /// <summary>İzin verilen azami offline aralık (Spec — "Connectivity and Offline Lease").</summary>
    public static readonly TimeSpan MaxOfflineInterval = TimeSpan.FromMinutes(15);

    private readonly TimeProvider _clock;
    private readonly object _sync = new();
    private DateTimeOffset _lastValidatedAt;
    private bool _connected = true;

    public ConnectivityLease(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        _lastValidatedAt = _clock.GetUtcNow();
    }

    /// <summary>Sunucu doğrulaması başarılı — kirayı yeniler ve bağlantıyı canlı işaretler.</summary>
    public void RecordServerValidation()
    {
        lock (_sync)
        {
            _lastValidatedAt = _clock.GetUtcNow();
            _connected = true;
        }
    }

    /// <summary>Bağlantı koptu. Kira SÜRESİ etkilenmez — yalnız yeni iş kabulü durur.</summary>
    public void MarkDisconnected()
    {
        lock (_sync)
        {
            _connected = false;
        }
    }

    /// <summary>Sunucuya bağlı mıyız? (Yeni iş kabulü bu bayrağa bakar.)</summary>
    public bool IsConnected { get { lock (_sync) { return _connected; } } }

    /// <summary>Kiranın sona ereceği an.</summary>
    public DateTimeOffset ExpiresAt { get { lock (_sync) { return _lastValidatedAt + MaxOfflineInterval; } } }

    /// <summary>
    /// Kira hâlâ geçerli mi? 14:59 geçerli, tam 15:00 geçersiz (sınır dışlayıcıdır).
    /// </summary>
    public bool IsValid => _clock.GetUtcNow() < ExpiresAt;

    /// <summary>Kalan süre (dolmuşsa <see cref="TimeSpan.Zero"/>).</summary>
    public TimeSpan Remaining
    {
        get
        {
            var left = ExpiresAt - _clock.GetUtcNow();
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }
}

/// <summary>
/// <see cref="IExecutionContinuationGate"/>'in kira tabanlı implementasyonu: kira geçerliyse
/// sonraki node başlar, dolmuşsa yürütme askıya alınır.
/// </summary>
public sealed class ConnectivityLeaseContinuationGate : IExecutionContinuationGate
{
    private readonly ConnectivityLease _lease;
    private readonly ILogger<ConnectivityLeaseContinuationGate>? _logger;

    public ConnectivityLeaseContinuationGate(
        ConnectivityLease lease,
        ILogger<ConnectivityLeaseContinuationGate>? logger = null)
    {
        _lease = lease ?? throw new ArgumentNullException(nameof(lease));
        _logger = logger;
    }

    /// <inheritdoc />
    public Task EnsureMayStartNodeAsync(Guid jobRunId, string nodeId, CancellationToken cancellationToken = default)
    {
        if (_lease.IsValid)
        {
            return Task.CompletedTask;
        }

        _logger?.LogWarning(
            "JobRun {JobRunId} — bağlantı kirası doldu ({ExpiresAt:o}); {NodeId} başlatılmadan askıya alınıyor.",
            jobRunId, _lease.ExpiresAt, nodeId);

        throw new ExecutionSuspendedException(
            jobRunId,
            nodeId,
            $"Bağlantı kirası doldu (azami {ConnectivityLease.MaxOfflineInterval.TotalMinutes:0} dk offline). " +
            $"'{nodeId}' başlatılmadı; bağlantı döndüğünde iş devam ettirilebilir.");
    }
}

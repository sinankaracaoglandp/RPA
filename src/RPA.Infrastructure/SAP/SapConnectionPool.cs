namespace RPA.Infrastructure.SAP;

using Microsoft.Extensions.Logging;
using RPA.Infrastructure.SAP.Nco;
using SystemException = RPA.Domain.Exceptions.SystemException;

/// <summary>
/// Thread-safe SAP RFC bağlantı havuzu (Task 4.2). Spec Bölüm 6 — bağlantılar platform
/// tarafından yönetilir, aktivite başına açılmaz.
///
/// Sorumluluklar:
/// - Ödünç alma/geri verme (<see cref="BorrowAsync"/> / <see cref="Return"/>) — boşta bağlantı
///   yeniden kullanılır, yoksa <see cref="SapNcoPoolOptions.MaxPoolSize"/>'a kadar yeni açılır.
/// - Yaşam döngüsü: ölü bağlantı atılır (<see cref="Discard"/>), COMMUNICATION_FAILURE'da
///   bağlantı açma yeniden denenir.
/// - Zaman aşımı: havuz doluyken bekleme <see cref="SapNcoPoolOptions.BorrowTimeoutSeconds"/>
///   ile sınırlandırılır; aşılırsa <see cref="SystemException"/>.
/// </summary>
public sealed class SapConnectionPool : IDisposable
{
    private readonly IRfcConnectionFactory _factory;
    private readonly SapConnectionSettings _settings;
    private readonly ILogger<SapConnectionPool> _logger;
    private readonly SemaphoreSlim _capacity;
    private readonly Stack<IRfcConnection> _idle = new();
    private readonly object _sync = new();
    private bool _disposed;

    public SapConnectionPool(
        IRfcConnectionFactory factory,
        SapConnectionSettings settings,
        SapNcoPoolOptions options,
        ILogger<SapConnectionPool> logger)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (Options.MaxPoolSize < 1)
        {
            throw new ArgumentException("MaxPoolSize en az 1 olmalı.", nameof(options));
        }

        _capacity = new SemaphoreSlim(Options.MaxPoolSize, Options.MaxPoolSize);
    }

    /// <summary>Havuz ayarları.</summary>
    public SapNcoPoolOptions Options { get; }

    /// <summary>Boşta bekleyen bağlantı sayısı.</summary>
    public int IdleCount
    {
        get { lock (_sync) { return _idle.Count; } }
    }

    /// <summary>Şu an ödünç verilmiş (aktif) bağlantı sayısı.</summary>
    public int ActiveCount => Options.MaxPoolSize - _capacity.CurrentCount;

    /// <summary>
    /// Havuzdan bir bağlantı ödünç al. Boşta canlı bağlantı varsa onu, yoksa yenisini açar.
    /// COMMUNICATION_FAILURE'da <see cref="SapNcoPoolOptions.MaxRetries"/> kadar yeniden dener.
    /// </summary>
    /// <exception cref="SystemException">Zaman aşımı veya tüm denemeler başarısız.</exception>
    public async Task<IRfcConnection> BorrowAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var acquired = await _capacity.WaitAsync(
            TimeSpan.FromSeconds(Options.BorrowTimeoutSeconds), cancellationToken);
        if (!acquired)
        {
            throw new SystemException(
                $"SAP bağlantı havuzu zaman aşımı ({Options.BorrowTimeoutSeconds}s) — tüm bağlantılar meşgul.");
        }

        try
        {
            // 1) Boşta canlı bir bağlantı yeniden kullan.
            while (TryPopIdle(out var pooled))
            {
                if (pooled!.IsAlive)
                {
                    return pooled;
                }

                _logger.LogDebug("Havuzdaki ölü bağlantı atılıyor: {Id}.", pooled.Id);
                SafeDispose(pooled);
            }

            // 2) Yeni bağlantı aç — iletişim hatasında yeniden dene.
            return await OpenWithRetryAsync(cancellationToken);
        }
        catch
        {
            _capacity.Release();
            throw;
        }
    }

    /// <summary>Bağlantıyı havuza geri ver (canlıysa yeniden kullanılır, değilse atılır).</summary>
    public void Return(IRfcConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (_disposed)
        {
            SafeDispose(connection);
            _capacity.Release();
            return;
        }

        if (connection.IsAlive)
        {
            lock (_sync)
            {
                _idle.Push(connection);
            }
        }
        else
        {
            SafeDispose(connection);
        }

        _capacity.Release();
    }

    /// <summary>Bağlantıyı havuzdan çıkar ve kapat (ör. iletişim hatası sonrası).</summary>
    public void Discard(IRfcConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        SafeDispose(connection);
        _capacity.Release();
    }

    private async Task<IRfcConnection> OpenWithRetryAsync(CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var conn = await _factory.OpenAsync(_settings, cancellationToken);
                _logger.LogDebug("Yeni SAP bağlantısı açıldı: {Id}.", conn.Id);
                return conn;
            }
            catch (RfcCommunicationException ex)
            {
                attempt++;
                if (attempt > Options.MaxRetries)
                {
                    throw new SystemException(
                        $"SAP bağlantısı açılamadı ({attempt} deneme, RFC_COMMUNICATION_FAILURE): {ex.Message}", ex);
                }

                _logger.LogWarning(
                    "SAP bağlantısı açılamadı (deneme {Attempt}/{Max}): {Message}. Yeniden deneniyor.",
                    attempt, Options.MaxRetries, ex.Message);
            }
        }
    }

    private bool TryPopIdle(out IRfcConnection? connection)
    {
        lock (_sync)
        {
            if (_idle.Count > 0)
            {
                connection = _idle.Pop();
                return true;
            }
        }

        connection = null;
        return false;
    }

    private void SafeDispose(IRfcConnection connection)
    {
        try
        {
            connection.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SAP bağlantısı kapatılırken hata: {Id}.", connection.Id);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        lock (_sync)
        {
            while (_idle.Count > 0)
            {
                SafeDispose(_idle.Pop());
            }
        }

        _capacity.Dispose();
    }
}

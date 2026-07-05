namespace RPA.Infrastructure.SAP;

using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.SAP.Nco;
using BusinessException = RPA.Domain.Exceptions.BusinessException;
using SystemException = RPA.Domain.Exceptions.SystemException;

/// <summary>
/// SAP NCo 3.1 programatik veri kanalı (Task 4.2) — <see cref="ISapDataChannel"/> implementasyonu.
/// Spec Bölüm 6: birincil (yüksek hacim) BAPI/RFC kanalı.
///
/// Bağlantılar <see cref="SapConnectionPool"/>'dan ödünç alınır; her çağrı için ödünç al →
/// çalıştır → geri ver döngüsü uygulanır. RFC iletişim hatasında bağlantı atılır ve çağrı
/// <see cref="SapNcoPoolOptions.MaxRetries"/> kadar yeniden denenir.
///
/// Exception sınıflandırması (CLAUDE.md Kısım 6):
/// - SAP dönüş türü E/A → <see cref="BusinessException"/> (iş kuralı, Action Center'a düşer).
/// - RFC_COMMUNICATION_FAILURE / timeout → <see cref="SystemException"/> (teknik, retry).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SapNcoChannel : ISapDataChannel
{
    private readonly SapConnectionPool _pool;
    private readonly ILogger<SapNcoChannel> _logger;

    public SapNcoChannel(SapConnectionPool pool, ILogger<SapNcoChannel> logger)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<SapCallResult> CallBapiAsync(
        string bapiName,
        Dictionary<string, object?> inputs,
        Dictionary<string, List<Dictionary<string, object?>>>? tableInputs = null,
        CancellationToken cancellationToken = default)
        => CallFunctionAsync(bapiName, inputs, tableInputs, throwOnBusinessError: true, cancellationToken);

    public Task<SapCallResult> CallRfcAsync(
        string rfcName,
        Dictionary<string, object?> inputs,
        Dictionary<string, List<Dictionary<string, object?>>>? tableInputs = null,
        CancellationToken cancellationToken = default)
        => CallFunctionAsync(rfcName, inputs, tableInputs, throwOnBusinessError: true, cancellationToken);

    public async Task<List<Dictionary<string, object?>>> ReadTableAsync(
        string tableName,
        List<string>? fields = null,
        string? where = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tableName))
        {
            throw new BusinessException("Tablo adı boş olamaz.");
        }

        var inputs = new Dictionary<string, object?>
        {
            ["QUERY_TABLE"] = tableName.Trim().ToUpperInvariant(),
            ["DELIMITER"] = "|",
        };

        var tables = new Dictionary<string, List<Dictionary<string, object?>>>
        {
            ["OPTIONS"] = RfcStructure.BuildOptions(where),
            ["FIELDS"] = RfcStructure.BuildFields(fields),
        };

        var result = await InvokeWithRetryAsync("RFC_READ_TABLE", inputs, tables, cancellationToken);
        return RfcStructure.ParseReadTableResult(result);
    }

    public Task<SapCallResult> CommitAsync(CancellationToken cancellationToken = default)
    {
        // BAPI_TRANSACTION_COMMIT — WAIT='X' senkron commit (spec 5.2 — explicit commit).
        var inputs = new Dictionary<string, object?> { ["WAIT"] = "X" };
        return CallFunctionAsync("BAPI_TRANSACTION_COMMIT", inputs, null, throwOnBusinessError: true, cancellationToken);
    }

    public Task<SapCallResult> RollbackAsync(CancellationToken cancellationToken = default)
        => CallFunctionAsync("BAPI_TRANSACTION_ROLLBACK", new Dictionary<string, object?>(), null,
            throwOnBusinessError: false, cancellationToken);

    public async Task<bool> IsHealthyAsync()
    {
        try
        {
            var conn = await _pool.BorrowAsync();
            _pool.Return(conn);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SAP NCo kanalı sağlık kontrolü başarısız.");
            return false;
        }
    }

    // =============================================================== iç yardımcılar

    private async Task<SapCallResult> CallFunctionAsync(
        string functionName,
        Dictionary<string, object?> inputs,
        Dictionary<string, List<Dictionary<string, object?>>>? tableInputs,
        bool throwOnBusinessError,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(functionName))
        {
            throw new BusinessException("BAPI/RFC adı boş olamaz.");
        }

        ArgumentNullException.ThrowIfNull(inputs);

        var result = await InvokeWithRetryAsync(
            functionName, inputs, tableInputs ?? new(), cancellationToken);

        var callResult = RfcStructure.ToCallResult(result);
        if (throwOnBusinessError && !callResult.Success)
        {
            throw new BusinessException(
                $"SAP {functionName} iş hatası ({callResult.ReturnType}): {callResult.Message}");
        }

        return callResult;
    }

    private async Task<RfcInvokeResult> InvokeWithRetryAsync(
        string functionName,
        IReadOnlyDictionary<string, object?> importing,
        IReadOnlyDictionary<string, List<Dictionary<string, object?>>> tables,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var connection = await _pool.BorrowAsync(cancellationToken);
            try
            {
                _logger.LogInformation("SAP NCo çağrı: {Function} ({ConnectionId}).", functionName, connection.Id);
                var result = await connection.InvokeAsync(functionName, importing, tables, cancellationToken);
                _pool.Return(connection);
                return result;
            }
            catch (RfcCommunicationException ex)
            {
                _pool.Discard(connection);
                attempt++;
                if (attempt > _pool.Options.MaxRetries)
                {
                    throw new SystemException(
                        $"SAP {functionName} RFC_COMMUNICATION_FAILURE ({attempt} deneme): {ex.Message}", ex);
                }

                _logger.LogWarning(
                    "SAP {Function} iletişim hatası (deneme {Attempt}/{Max}): {Message}. Yeniden deneniyor.",
                    functionName, attempt, _pool.Options.MaxRetries, ex.Message);
            }
            catch (Exception ex) when (ex is not BusinessException and not SystemException and not OperationCanceledException)
            {
                _pool.Discard(connection);
                throw new SystemException($"SAP {functionName} beklenmeyen hata: {ex.Message}", ex);
            }
        }
    }
}

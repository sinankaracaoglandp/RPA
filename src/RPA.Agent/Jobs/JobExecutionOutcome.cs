namespace RPA.Agent.Jobs;

using RPA.Domain.Enums;

/// <summary>İş çalıştırma sonucu: başarı/başarısızlık, çıktı değerleri, istisna sınıflandırması.</summary>
public sealed class JobExecutionOutcome
{
    private JobExecutionOutcome() { }

    public bool Success { get; private init; }
    public Dictionary<string, object?> Outputs { get; private init; } = new();
    public Exception? Exception { get; private init; }
    public ExceptionType? ExceptionType { get; private init; }
    public long DurationMs { get; private init; }

    /// <summary>İş kuralı istisnası mı? (true → kuyruk retry etmez.)</summary>
    public bool IsBusinessException => ExceptionType == RPA.Domain.Enums.ExceptionType.Business;

    public static JobExecutionOutcome Succeeded(Dictionary<string, object?> outputs, long durationMs)
        => new() { Success = true, Outputs = outputs ?? new(), DurationMs = durationMs };

    public static JobExecutionOutcome Failed(Exception exception, ExceptionType type, long durationMs)
        => new() { Success = false, Exception = exception, ExceptionType = type, DurationMs = durationMs };
}

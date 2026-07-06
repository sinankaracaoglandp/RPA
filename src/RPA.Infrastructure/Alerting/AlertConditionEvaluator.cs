namespace RPA.Infrastructure.Alerting;

using System.Text.Json;
using RPA.Domain.Entities;

/// <summary>Bir değerlendirme penceresindeki alarm metrikleri anlık görüntüsü (WP-6.3).</summary>
public sealed record AlertMetrics(
    int SystemExceptionCount,
    int BusinessExceptionCount,
    int RobotOfflineCount,
    int QueueSlaBreachCount);

/// <summary>Kural değerlendirme sonucu.</summary>
public sealed record AlertEvaluation(bool Triggered, string Message);

/// <summary>
/// AlertRule.Condition JSON'ını ({ "metric": "...", "threshold": N }) verilen metriklere karşı
/// değerlendirir. Bilinmeyen metrik veya bozuk JSON güvenli biçimde "tetiklenmedi" döner (WP-6.3).
/// </summary>
public sealed class AlertConditionEvaluator
{
    private sealed record Condition(string? Metric, int Threshold);

    public AlertEvaluation Evaluate(AlertRule rule, AlertMetrics metrics)
    {
        Condition? condition;
        try
        {
            condition = JsonSerializer.Deserialize<Condition>(
                rule.Condition,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return new AlertEvaluation(false, string.Empty);
        }

        if (condition?.Metric is null)
        {
            return new AlertEvaluation(false, string.Empty);
        }

        var actual = condition.Metric switch
        {
            "SystemExceptionCount" => metrics.SystemExceptionCount,
            "BusinessExceptionCount" => metrics.BusinessExceptionCount,
            "RobotOfflineCount" => metrics.RobotOfflineCount,
            "QueueSlaBreachCount" => metrics.QueueSlaBreachCount,
            _ => (int?)null,
        } ?? -1;

        if (actual < 0 || actual < condition.Threshold)
        {
            return new AlertEvaluation(false, string.Empty);
        }

        var message =
            $"[{rule.Name}] {condition.Metric} = {actual} (eşik: {condition.Threshold}) — alarm tetiklendi.";
        return new AlertEvaluation(true, message);
    }
}

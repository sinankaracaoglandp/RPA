namespace RPA.Infrastructure.Tests;

using RPA.Domain.Entities;
using RPA.Infrastructure.Alerting;

/// <summary>
/// WP-6.3 — Alerting motoru: koşul değerlendirme (SystemException eşiği, Business birikim,
/// robot offline, kuyruk SLA) ve tetiklenen kurallar için bildirim mesajı üretimi. Spec Bölüm 8.2, 11.
/// </summary>
public class AlertEngineTests
{
    private static AlertRule Rule(string metric, int threshold) => new()
    {
        Name = $"{metric} alarmı",
        Condition = $"{{\"metric\":\"{metric}\",\"threshold\":{threshold}}}",
        Channel = "email",
        Recipients = "ops@example.com",
        IsActive = true,
    };

    [Fact]
    public void Evaluate_Triggers_WhenMetricMeetsThreshold()
    {
        var evaluator = new AlertConditionEvaluator();
        var metrics = new AlertMetrics(SystemExceptionCount: 5, BusinessExceptionCount: 0, RobotOfflineCount: 0, QueueSlaBreachCount: 0);

        var result = evaluator.Evaluate(Rule("SystemExceptionCount", 5), metrics);

        Assert.True(result.Triggered);
        Assert.Contains("5", result.Message);
    }

    [Fact]
    public void Evaluate_DoesNotTrigger_BelowThreshold()
    {
        var evaluator = new AlertConditionEvaluator();
        var metrics = new AlertMetrics(0, BusinessExceptionCount: 3, 0, 0);

        var result = evaluator.Evaluate(Rule("BusinessExceptionCount", 10), metrics);

        Assert.False(result.Triggered);
    }

    [Fact]
    public void Evaluate_RobotOffline_UsesCorrectMetric()
    {
        var evaluator = new AlertConditionEvaluator();
        var metrics = new AlertMetrics(0, 0, RobotOfflineCount: 2, 0);

        Assert.True(evaluator.Evaluate(Rule("RobotOfflineCount", 1), metrics).Triggered);
    }

    [Fact]
    public void Evaluate_UnknownMetric_DoesNotTriggerAndIsSafe()
    {
        var evaluator = new AlertConditionEvaluator();
        var metrics = new AlertMetrics(9, 9, 9, 9);

        var result = evaluator.Evaluate(Rule("Nonsense", 1), metrics);

        Assert.False(result.Triggered);
    }

    [Fact]
    public void Evaluate_MalformedCondition_DoesNotThrow()
    {
        var evaluator = new AlertConditionEvaluator();
        var rule = new AlertRule { Name = "bozuk", Condition = "not-json", IsActive = true };

        var result = evaluator.Evaluate(rule, new AlertMetrics(1, 1, 1, 1));

        Assert.False(result.Triggered);
    }
}

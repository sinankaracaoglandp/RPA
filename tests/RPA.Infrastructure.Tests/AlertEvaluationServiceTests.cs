namespace RPA.Infrastructure.Tests;

using RPA.Domain.Entities;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Alerting;

/// <summary>
/// WP-6.3 — AlertEvaluationService: aktif kuralları metriklere karşı değerlendirir ve tetiklenen
/// her kural için ilgili kanala bildirim gönderir.
/// </summary>
public class AlertEvaluationServiceTests
{
    private sealed class FakeRepo : IAlertRuleRepository
    {
        public readonly List<AlertRule> Rules = new();
        public Task<IReadOnlyList<AlertRule>> ListActiveAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AlertRule>>(Rules.Where(r => r.IsActive).ToList());
        public Task<IReadOnlyList<AlertRule>> ListAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AlertRule>>(Rules.ToList());
        public Task<AlertRule?> FindByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(Rules.FirstOrDefault(r => r.Id == id));
        public Task AddAsync(AlertRule rule, CancellationToken ct = default) { Rules.Add(rule); return Task.CompletedTask; }
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class RecordingSender : INotificationSender
    {
        public readonly List<(string channel, string recipients, string message)> Sent = new();
        public Task SendAsync(string channel, string recipients, string message, CancellationToken ct = default)
        {
            Sent.Add((channel, recipients, message));
            return Task.CompletedTask;
        }
    }

    private static AlertRule Rule(string metric, int threshold, string channel = "email", bool active = true) => new()
    {
        Name = $"{metric}",
        Condition = $"{{\"metric\":\"{metric}\",\"threshold\":{threshold}}}",
        Channel = channel,
        Recipients = "ops@example.com",
        IsActive = active,
    };

    [Fact]
    public async Task Dispatch_SendsNotification_ForTriggeredRuleOnly()
    {
        var repo = new FakeRepo();
        repo.Rules.Add(Rule("SystemExceptionCount", 5));   // tetiklenir (actual 6)
        repo.Rules.Add(Rule("BusinessExceptionCount", 100)); // tetiklenmez
        var sender = new RecordingSender();
        var svc = new AlertEvaluationService(repo, new AlertConditionEvaluator(), sender);

        var fired = await svc.EvaluateAndDispatchAsync(new AlertMetrics(6, 3, 0, 0));

        Assert.Equal(1, fired);
        Assert.Single(sender.Sent);
        Assert.Equal("email", sender.Sent[0].channel);
        Assert.Contains("SystemExceptionCount", sender.Sent[0].message);
    }

    [Fact]
    public async Task Dispatch_IgnoresInactiveRules()
    {
        var repo = new FakeRepo();
        repo.Rules.Add(Rule("RobotOfflineCount", 1, active: false));
        var sender = new RecordingSender();
        var svc = new AlertEvaluationService(repo, new AlertConditionEvaluator(), sender);

        var fired = await svc.EvaluateAndDispatchAsync(new AlertMetrics(0, 0, 5, 0));

        Assert.Equal(0, fired);
        Assert.Empty(sender.Sent);
    }
}

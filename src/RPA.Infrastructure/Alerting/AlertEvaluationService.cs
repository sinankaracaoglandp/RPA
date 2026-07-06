namespace RPA.Infrastructure.Alerting;

using RPA.Domain.Interfaces;

/// <summary>
/// Alarm değerlendirme servisi (WP-6.3, Spec Bölüm 8.2, 11). Aktif kuralları verilen metrik
/// anlık görüntüsüne karşı değerlendirir; tetiklenen her kural için ilgili kanala
/// (e-posta/Teams) bildirim gönderir. Tetiklenen kural sayısını döner.
/// </summary>
public sealed class AlertEvaluationService
{
    private readonly IAlertRuleRepository _repository;
    private readonly AlertConditionEvaluator _evaluator;
    private readonly INotificationSender _sender;

    public AlertEvaluationService(
        IAlertRuleRepository repository,
        AlertConditionEvaluator evaluator,
        INotificationSender sender)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
    }

    public async Task<int> EvaluateAndDispatchAsync(
        AlertMetrics metrics, CancellationToken cancellationToken = default)
    {
        var rules = await _repository.ListActiveAsync(cancellationToken).ConfigureAwait(false);
        var fired = 0;

        foreach (var rule in rules)
        {
            var result = _evaluator.Evaluate(rule, metrics);
            if (!result.Triggered)
            {
                continue;
            }

            await _sender.SendAsync(rule.Channel, rule.Recipients, result.Message, cancellationToken)
                .ConfigureAwait(false);
            fired++;
        }

        return fired;
    }
}

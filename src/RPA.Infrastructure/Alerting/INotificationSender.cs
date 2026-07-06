namespace RPA.Infrastructure.Alerting;

/// <summary>
/// Alarm bildirim gönderimi (WP-6.3). Kanal "email" veya "teams" (webhook URL). Uygulamalar
/// kanal-özgü taşıma sağlar; motor kanaldan bağımsızdır.
/// </summary>
public interface INotificationSender
{
    Task SendAsync(string channel, string recipients, string message, CancellationToken cancellationToken = default);
}

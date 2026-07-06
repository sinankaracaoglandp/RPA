namespace RPA.Infrastructure.Alerting;

using System.Net.Http.Json;

/// <summary>E-posta alarm gönderimi seam'i (WP-6.3). SMTP implementasyonu DI ile verilir.</summary>
public interface IAlertEmailSender
{
    Task SendAsync(string recipients, string subject, string body, CancellationToken cancellationToken = default);
}

/// <summary>
/// Kanala göre alarm bildirimi yönlendirir (WP-6.3): "teams" → webhook(ler)e HTTP POST ({ text }),
/// "email" → <see cref="IAlertEmailSender"/>. Bilinmeyen kanal sessizce yok sayılır.
/// Recipients virgülle ayrılmış (Teams için webhook URL'leri, email için adresler).
/// </summary>
public sealed class ChannelNotificationSender : INotificationSender
{
    private readonly HttpClient _http;
    private readonly IAlertEmailSender _email;

    public ChannelNotificationSender(HttpClient http, IAlertEmailSender email)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _email = email ?? throw new ArgumentNullException(nameof(email));
    }

    public async Task SendAsync(
        string channel, string recipients, string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(recipients))
        {
            return;
        }

        switch ((channel ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "teams":
                foreach (var url in Split(recipients))
                {
                    await _http.PostAsJsonAsync(url, new { text = message }, cancellationToken)
                        .ConfigureAwait(false);
                }
                break;

            case "email":
                await _email.SendAsync(recipients, "RPA Alarm", message, cancellationToken)
                    .ConfigureAwait(false);
                break;
        }
    }

    private static IEnumerable<string> Split(string csv)
        => csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

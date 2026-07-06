namespace RPA.Infrastructure.Alerting;

using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;

/// <summary>Alarm e-posta gönderimi için SMTP ayarları (WP-6.3). Config'ten bağlanır (Alerting:Smtp).</summary>
public sealed class AlertEmailOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string FromAddress { get; set; } = "rpa-alerts@localhost";
    public string? Username { get; set; }
    public string? Password { get; set; }
}

/// <summary>
/// MailKit tabanlı alarm e-posta göndericisi (WP-6.3). Recipients virgülle ayrılmış adreslerdir.
/// Host boşsa gönderim atlanır (yapılandırılmamış ortamda motor sessizce çalışır).
/// </summary>
public sealed class SmtpAlertEmailSender : IAlertEmailSender
{
    private readonly AlertEmailOptions _options;
    private readonly ILogger<SmtpAlertEmailSender> _logger;

    public SmtpAlertEmailSender(AlertEmailOptions options, ILogger<SmtpAlertEmailSender> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SendAsync(
        string recipients, string subject, string body, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Host))
        {
            _logger.LogWarning("Alarm e-postası atlandı: SMTP host yapılandırılmamış.");
            return;
        }

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(_options.FromAddress));
        foreach (var to in recipients.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            message.To.Add(MailboxAddress.Parse(to));
        }
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient();
        var tls = _options.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;
        await client.ConnectAsync(_options.Host, _options.Port, tls, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(_options.Username))
        {
            await client.AuthenticateAsync(_options.Username, _options.Password, cancellationToken).ConfigureAwait(false);
        }
        await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
        await client.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);
    }
}

namespace RPA.Infrastructure.Workflow.Activities.Email;

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using RPA.Domain.Exceptions;
using RPA.Domain.Interfaces;
using SystemException = RPA.Domain.Exceptions.SystemException;

/// <summary>
/// E-posta gönderme aktivitesi (SMTP/MailKit).
/// Spec Bölüm 5.3: "E-posta: Gönder (SMTP/EWS)".
/// </summary>
public class EmailSendActivity : IActivity
{
    private readonly ILogger<EmailSendActivity> _logger;

    public EmailSendActivity(ILogger<EmailSendActivity> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ActivityMetadata GetMetadata()
    {
        return new ActivityMetadata
        {
            ActivityId = "Email.Send",
            DisplayName = "E-posta Gönder",
            Category = "Email",
            Description = "SMTP/EWS ile e-posta gönderir.",
            Inputs = new List<ActivityParameter>
            {
                new() { Name = "to", Type = "string", Required = true, Description = "Alıcı e-posta adresi" },
                new() { Name = "subject", Type = "string", Required = true, Description = "E-posta konusu" },
                new() { Name = "body", Type = "string", Required = true, Description = "E-posta gövdesi (HTML desteklenir)" },
                new() { Name = "attachments", Type = "JSON", Required = false, Description = "Ek dosya yolları (JSON array)" },
                new() { Name = "credentialName", Type = "Credential", Required = true, Description = "SMTP credential referansı (formato: host:port:username:password)" }
            },
            Outputs = new List<ActivityParameter>(),
            RequiredCapabilities = new List<string> { "email" }
        };
    }

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        try
        {
            context.Log("E-posta gönderme başlatıldı.", RPA.Domain.Interfaces.LogLevel.Information);

            // Parametreleri context'ten al
            string to = context.GetVariable<string>("to");
            string subject = context.GetVariable<string>("subject");
            string body = context.GetVariable<string>("body");
            string credentialName = context.GetVariable<string>("credentialName");

            if (string.IsNullOrWhiteSpace(to))
                throw new BusinessException("E-posta alıcısı (to) boş olamaz.");
            if (string.IsNullOrWhiteSpace(subject))
                throw new BusinessException("E-posta konusu (subject) boş olamaz.");
            if (string.IsNullOrWhiteSpace(body))
                throw new BusinessException("E-posta gövdesi (body) boş olamaz.");

            // Credential'ı vault'tan al (şifreli)
            string credential = await context.GetCredentialAsync(credentialName);
            // Beklenen format: host:port:username:password
            var parts = credential.Split(':', 4);
            if (parts.Length < 4)
                throw new BusinessException($"Credential '{credentialName}' hatalı formatta. Beklenen: host:port:username:password");

            string host = parts[0];
            if (!int.TryParse(parts[1], out int port))
                throw new BusinessException($"Credential '{credentialName}' içindeki port sayı değil: {parts[1]}");
            string username = parts[2];
            string password = parts[3];

            // Ek dosyaları al (varsa)
            List<string> attachmentPaths = new();
            try
            {
                var attachmentVar = context.GetVariable<object>("attachments");
                if (attachmentVar != null)
                {
                    if (attachmentVar is JsonElement elem)
                    {
                        foreach (var item in elem.EnumerateArray())
                            attachmentPaths.Add(item.GetString() ?? "");
                    }
                    else if (attachmentVar is string[] arr)
                    {
                        attachmentPaths.AddRange(arr);
                    }
                    else if (attachmentVar is List<string> list)
                    {
                        attachmentPaths.AddRange(list);
                    }
                }
            }
            catch
            {
                // Ek dosya yok, devam et
            }

            // MIME mesajı oluştur
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("RPA", username));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder
            {
                HtmlBody = body
            };

            // Ek dosyaları ekle
            foreach (var path in attachmentPaths)
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    try
                    {
                        bodyBuilder.Attachments.Add(path);
                    }
                    catch (Exception ex)
                    {
                        context.Log($"Ek dosya eklenemedi: {path} — {ex.Message}", RPA.Domain.Interfaces.LogLevel.Warning);
                    }
                }
            }

            message.Body = bodyBuilder.ToMessageBody();

            // SMTP ile gönder
            using (var client = new SmtpClient())
            {
                try
                {
                    context.Log($"SMTP bağlantısı: {host}:{port}", RPA.Domain.Interfaces.LogLevel.Debug);

                    // HIGH FIX: Set 30-second timeout to prevent indefinite connection hold
                    client.Timeout = 30000; // milliseconds

                    // Port numarasına göre otomatik TLS ayarla
                    SecureSocketOptions options = port == 587 ? SecureSocketOptions.StartTls :
                                                  port == 465 ? SecureSocketOptions.SslOnConnect :
                                                  SecureSocketOptions.None;

                    await client.ConnectAsync(host, port, options);

                    // HIGH FIX: Clear password from memory after authentication
                    var passwordBytes = Encoding.UTF8.GetBytes(password);
                    try
                    {
                        await client.AuthenticateAsync(username, password);
                    }
                    finally
                    {
                        // Clear password bytes from memory
                        Array.Clear(passwordBytes, 0, passwordBytes.Length);
                    }

                    await client.SendAsync(message);
                    await client.DisconnectAsync(true);

                    context.Log($"E-posta başarıyla gönderildi: {to}", RPA.Domain.Interfaces.LogLevel.Information);
                }
                catch (Exception ex) when (!(ex is BusinessException))
                {
                    throw new SystemException($"SMTP hatası: {ex.Message}", ex);
                }
            }

            return new Dictionary<string, object?>();
        }
        catch (BusinessException)
        {
            throw;
        }
        catch (SystemException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new SystemException($"E-posta gönderme sırasında hata: {ex.Message}", ex);
        }
    }
}

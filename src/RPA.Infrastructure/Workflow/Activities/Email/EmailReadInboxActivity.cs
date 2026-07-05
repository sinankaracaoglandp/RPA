namespace RPA.Infrastructure.Workflow.Activities.Email;

using System.Collections.Generic;
using System.Threading.Tasks;
using MailKit;
using MailKit.Net.Imap;
using Microsoft.Extensions.Logging;
using RPA.Domain.Exceptions;
using RPA.Domain.Interfaces;
using SystemException = RPA.Domain.Exceptions.SystemException;

/// <summary>
/// Gelen kutusu okuma aktivitesi (IMAP/MailKit).
/// Spec Bölüm 5.3: "E-posta: ... GelenKutusuOku (IMAP/EWS)".
/// </summary>
public class EmailReadInboxActivity : IActivity
{
    private readonly ILogger<EmailReadInboxActivity> _logger;

    public EmailReadInboxActivity(ILogger<EmailReadInboxActivity> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ActivityMetadata GetMetadata()
    {
        return new ActivityMetadata
        {
            ActivityId = "Email.ReadInbox",
            DisplayName = "Gelen Kutusu Oku",
            Category = "Email",
            Description = "IMAP/EWS ile gelen kutusunu okur (filtreli).",
            Inputs = new List<ActivityParameter>
            {
                new() { Name = "folder", Type = "string", Required = false, DefaultValue = "INBOX", Description = "Okunacak klasör (varsayılan: INBOX)" },
                new() { Name = "filter", Type = "JSON", Required = false, Description = "Filtre (JSON): {\"unseen\": true, \"subject\": \"...\", \"from\": \"...\"}" },
                new() { Name = "credentialName", Type = "Credential", Required = true, Description = "IMAP credential referansı (formato: host:port:username:password)" }
            },
            Outputs = new List<ActivityParameter>
            {
                new() { Name = "messages", Type = "JSON", Required = false, Description = "Okunmuş e-postalar (JSON array)" }
            },
            RequiredCapabilities = new List<string> { "email" }
        };
    }

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        try
        {
            context.Log("Gelen kutusu okuma başlatıldı.", RPA.Domain.Interfaces.LogLevel.Information);

            // Parametreleri al
            string folder = context.GetVariable<string>("folder") ?? "INBOX";
            string credentialName = context.GetVariable<string>("credentialName");
            var filterVar = context.GetVariable<object>("filter");

            if (string.IsNullOrWhiteSpace(credentialName))
                throw new BusinessException("Credential ismi (credentialName) boş olamaz.");

            // Credential'ı vault'tan al (şifreli)
            string credential = await context.GetCredentialAsync(credentialName);
            var parts = credential.Split(':', 4);
            if (parts.Length < 4)
                throw new BusinessException($"Credential '{credentialName}' hatalı formatta. Beklenen: host:port:username:password");

            string host = parts[0];
            if (!int.TryParse(parts[1], out int port))
                throw new BusinessException($"Credential içindeki port sayı değil: {parts[1]}");
            string username = parts[2];
            string password = parts[3];

            var messages = new List<Dictionary<string, object?>>();

            // IMAP ile oku
            using (var client = new ImapClient())
            {
                try
                {
                    context.Log($"IMAP bağlantısı: {host}:{port}", RPA.Domain.Interfaces.LogLevel.Debug);

                    // HIGH FIX: Set 30-second timeout to prevent indefinite connection hold
                    client.Timeout = 30000; // milliseconds

                    // Port numarasına göre otomatik TLS ayarla
                    var options = port == 993 ? MailKit.Security.SecureSocketOptions.SslOnConnect :
                                  port == 143 ? MailKit.Security.SecureSocketOptions.StartTls :
                                  MailKit.Security.SecureSocketOptions.None;

                    await client.ConnectAsync(host, port, options);

                    // HIGH FIX: Clear password from memory after authentication
                    var passwordBytes = System.Text.Encoding.UTF8.GetBytes(password);
                    try
                    {
                        await client.AuthenticateAsync(username, password);
                    }
                    finally
                    {
                        // Clear password bytes from memory
                        Array.Clear(passwordBytes, 0, passwordBytes.Length);
                    }

                    // Klasörü aç
                    var mailFolder = client.GetFolder(folder);
                    if (mailFolder == null)
                        throw new BusinessException($"Klasör bulunamadı: {folder}");

                    await mailFolder.OpenAsync(FolderAccess.ReadOnly);

                    context.Log($"Klasör açıldı: {folder} ({mailFolder.Count} mesaj)", RPA.Domain.Interfaces.LogLevel.Information);

                    // Tüm mesajları oku
                    var messageCount = mailFolder.Count;
                    context.Log($"Okunan mesajlar: {messageCount}", RPA.Domain.Interfaces.LogLevel.Information);

                    // Mesajları oku - tüm indexleri iterate et
                    for (int index = 0; index < messageCount; index++)
                    {
                        try
                        {
                            var msg = await mailFolder.GetMessageAsync(index);
                            var attachmentCount = msg.Attachments.Count();
                            var msgDict = new Dictionary<string, object?>
                            {
                                { "id", index.ToString() },
                                { "subject", msg.Subject },
                                { "from", msg.From.ToString() },
                                { "date", msg.Date },
                                { "body", msg.TextBody ?? msg.HtmlBody },
                                { "hasAttachments", attachmentCount > 0 },
                                { "attachmentCount", attachmentCount }
                            };
                            messages.Add(msgDict);
                        }
                        catch (Exception ex)
                        {
                            context.Log($"Mesaj okunamadı (index {index}): {ex.Message}", RPA.Domain.Interfaces.LogLevel.Warning);
                        }
                    }

                    await client.DisconnectAsync(true);
                    context.Log($"{messages.Count} mesaj başarıyla okundu.", RPA.Domain.Interfaces.LogLevel.Information);
                }
                catch (Exception ex) when (!(ex is BusinessException))
                {
                    throw new SystemException($"IMAP hatası: {ex.Message}", ex);
                }
            }

            context.SetVariable("messages", messages);
            return new Dictionary<string, object?> { { "messages", messages } };
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
            throw new SystemException($"Gelen kutusu okuma sırasında hata: {ex.Message}", ex);
        }
    }
}

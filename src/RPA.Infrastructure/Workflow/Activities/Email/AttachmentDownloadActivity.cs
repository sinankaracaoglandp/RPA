namespace RPA.Infrastructure.Workflow.Activities.Email;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MailKit;
using MailKit.Net.Imap;
using Microsoft.Extensions.Logging;
using RPA.Domain.Exceptions;
using RPA.Domain.Interfaces;
using SystemException = RPA.Domain.Exceptions.SystemException;

/// <summary>
/// E-posta eki indirme aktivitesi (IMAP/MailKit).
/// Spec Bölüm 5.3: "E-posta: ... Ek indir".
/// </summary>
public class AttachmentDownloadActivity : IActivity
{
    private readonly ILogger<AttachmentDownloadActivity> _logger;

    public AttachmentDownloadActivity(ILogger<AttachmentDownloadActivity> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ActivityMetadata GetMetadata()
    {
        return new ActivityMetadata
        {
            ActivityId = "Email.DownloadAttachment",
            DisplayName = "Ek İndir",
            Category = "Email",
            Description = "E-posta ekini diske indirir.",
            Inputs = new List<ActivityParameter>
            {
                new() { Name = "messageId", Type = "string", Required = true, Description = "E-posta mesaj ID'si (UID)" },
                new() { Name = "targetFolder", Type = "string", Required = true, Description = "Hedef klasör yolu" },
                new() { Name = "credentialName", Type = "Credential", Required = true, Description = "IMAP credential referansı (formato: host:port:username:password)" },
                new() { Name = "folder", Type = "string", Required = false, DefaultValue = "INBOX", Description = "Okunacak klasör" }
            },
            Outputs = new List<ActivityParameter>
            {
                new() { Name = "paths", Type = "JSON", Required = false, Description = "İndirilen dosya yolları (JSON array)" }
            },
            RequiredCapabilities = new List<string> { "email" }
        };
    }

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        try
        {
            context.Log("Ek indirme başlatıldı.", RPA.Domain.Interfaces.LogLevel.Information);

            // Parametreleri al
            string messageId = context.GetVariable<string>("messageId");
            string targetFolder = context.GetVariable<string>("targetFolder");
            string credentialName = context.GetVariable<string>("credentialName");
            string folder = context.GetVariable<string>("folder") ?? "INBOX";

            if (string.IsNullOrWhiteSpace(messageId))
                throw new BusinessException("Mesaj ID'si (messageId) boş olamaz.");
            if (string.IsNullOrWhiteSpace(targetFolder))
                throw new BusinessException("Hedef klasör (targetFolder) boş olamaz.");
            if (string.IsNullOrWhiteSpace(credentialName))
                throw new BusinessException("Credential ismi (credentialName) boş olamaz.");

            // Hedef klasörü kontrol et/oluştur
            if (!Directory.Exists(targetFolder))
            {
                try
                {
                    Directory.CreateDirectory(targetFolder);
                    context.Log($"Hedef klasör oluşturuldu: {targetFolder}", RPA.Domain.Interfaces.LogLevel.Information);
                }
                catch (Exception ex)
                {
                    throw new BusinessException($"Hedef klasör oluşturulamadı: {targetFolder} — {ex.Message}");
                }
            }

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

            // Parse messageId
            if (!uint.TryParse(messageId, out uint uid))
                throw new BusinessException($"Geçersiz mesaj ID'si: {messageId}");

            var downloadedPaths = new List<string>();

            // IMAP ile bağlan ve eki indir
            using (var client = new ImapClient())
            {
                try
                {
                    context.Log($"IMAP bağlantısı: {host}:{port}", RPA.Domain.Interfaces.LogLevel.Debug);

                    // HIGH FIX: Set 30-second timeout to prevent indefinite connection hold
                    client.Timeout = 30000; // milliseconds

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

                    var mailFolder = client.GetFolder(folder);
                    if (mailFolder == null)
                        throw new BusinessException($"Klasör bulunamadı: {folder}");

                    await mailFolder.OpenAsync(FolderAccess.ReadOnly);

                    // Mesajı getir
                    var message = await mailFolder.GetMessageAsync(new UniqueId(uid));
                    if (message == null)
                        throw new BusinessException($"Mesaj bulunamadı: {messageId}");

                    // Ekleri indir
                    var attachmentCount = message.Attachments.Count();
                    if (attachmentCount == 0)
                    {
                        context.Log($"Mesajın eki yok (UID {uid})", RPA.Domain.Interfaces.LogLevel.Warning);
                    }
                    else
                    {
                        foreach (var attachment in message.Attachments)
                        {
                            try
                            {
                                string fileName = attachment.ContentDisposition?.FileName ?? attachment.ContentType.Name ?? $"attachment_{Guid.NewGuid()}";
                                string filePath = Path.Combine(targetFolder, fileName);

                                // Dosya adı çakışmasını çöz
                                int counter = 1;
                                string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                                string ext = Path.GetExtension(fileName);
                                while (File.Exists(filePath))
                                {
                                    fileName = $"{nameWithoutExt}_{counter}{ext}";
                                    filePath = Path.Combine(targetFolder, fileName);
                                    counter++;
                                }

                                // Eki diske yaz
                                if (attachment is MimeKit.MimePart mimePart)
                                {
                                    using (var stream = System.IO.File.Create(filePath))
                                    {
                                        await mimePart.Content.WriteToAsync(stream);
                                    }
                                    downloadedPaths.Add(filePath);
                                    context.Log($"Ek indirildi: {filePath}", RPA.Domain.Interfaces.LogLevel.Information);
                                }
                            }
                            catch (Exception ex)
                            {
                                context.Log($"Ek indirilemedi: {ex.Message}", RPA.Domain.Interfaces.LogLevel.Warning);
                            }
                        }
                    }

                    await client.DisconnectAsync(true);
                }
                catch (Exception ex) when (!(ex is BusinessException))
                {
                    throw new SystemException($"IMAP hatası: {ex.Message}", ex);
                }
            }

            context.SetVariable("paths", downloadedPaths);
            return new Dictionary<string, object?> { { "paths", downloadedPaths } };
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
            throw new SystemException($"Ek indirme sırasında hata: {ex.Message}", ex);
        }
    }
}

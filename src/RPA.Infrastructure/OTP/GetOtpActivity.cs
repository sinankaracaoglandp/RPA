namespace RPA.Infrastructure.OTP;

using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;
using BusinessException = RPA.Domain.Exceptions.BusinessException;

/// <summary>
/// OTP/2FA kodu alma aktivitesi (Spec Bölüm 7). Bir veya daha fazla kanalı <b>sırayla</b> dener:
/// kanal N zaman aşımına uğrar/başarısız olursa kanal N+1'e geçer. Tüm kanallar başarısızsa
/// <see cref="BusinessException"/> fırlatır. Alınan kod DB audit'i için AES ile şifrelenir,
/// loglarda daima "[MASKED]" gösterilir. Entity TTL varsayılan 5 dk.
/// </summary>
public sealed class GetOtpActivity : IActivity
{
    private const int DefaultTimeoutSeconds = 180; // 3 dk (Spec Bölüm 7)
    private static readonly TimeSpan CodeTtl = TimeSpan.FromMinutes(5);

    private readonly IOtpChannelFactory _factory;
    private readonly IOtpCodeProtector _protector;
    private readonly IOtpAuditSink _audit;

    public GetOtpActivity(IOtpChannelFactory factory, IOtpCodeProtector protector, IOtpAuditSink audit)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    public ActivityMetadata GetMetadata()
    {
        return new ActivityMetadata
        {
            ActivityId = "Otp.Get",
            DisplayName = "OTP Al",
            Category = "Güvenlik",
            Description = "Portal girişi için OTP/2FA kodunu çok kanallı fallback ile alır.",
            Inputs = new()
            {
                new ActivityParameter { Name = "portalReference", Type = "string", Required = true, Description = "Portal URL'i / kimliği" },
                new ActivityParameter { Name = "channels", Type = "JSON", Required = true, Description = "Denenecek kanallar (sırayla): Email, Totp, GsmModem, PhoneForward, HumanApproval" },
                new ActivityParameter { Name = "timeoutSeconds", Type = "int", Required = false, DefaultValue = DefaultTimeoutSeconds, Description = "Kanal başına zaman aşımı (sn)" }
            },
            Outputs = new()
            {
                new ActivityParameter { Name = "otpCode", Type = "Credential", Required = false, Description = "Alınan OTP kodu (runtime — loglarda maskeli)" },
                new ActivityParameter { Name = "channelUsed", Type = "string", Required = false, Description = "Kodu sağlayan kanal" },
                new ActivityParameter { Name = "otpRequestId", Type = "string", Required = false, Description = "OtpRequest audit kaydı ID'si" }
            },
            RequiredCapabilities = new() { "otp" },
            ExceptionClassification = new ExceptionClassificationRule
            {
                Condition = "AllChannelsFailed",
                Classification = ExceptionType.Business
            }
        };
    }

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var portalReference = context.GetVariable<string>("portalReference");
        if (string.IsNullOrWhiteSpace(portalReference))
        {
            throw new BusinessException("'portalReference' parametresi boş olamaz.");
        }

        var channels = ResolveChannels(context);
        if (channels.Count == 0)
        {
            throw new BusinessException("'channels' parametresi en az bir kanal içermelidir.");
        }

        var timeoutSeconds = context.GetVariable<int?>("timeoutSeconds") ?? DefaultTimeoutSeconds;
        if (timeoutSeconds <= 0)
        {
            timeoutSeconds = DefaultTimeoutSeconds;
        }

        var timeout = TimeSpan.FromSeconds(timeoutSeconds);
        var request = new OtpRequest
        {
            JobRunId = context.JobRunId,
            PortalReference = portalReference,
            Status = OtpRequestStatus.Pending,
            ExpiresAt = DateTime.UtcNow.Add(CodeTtl)
        };

        foreach (var channelType in channels)
        {
            if (!_factory.Supports(channelType))
            {
                context.Log($"OTP kanalı kayıtlı değil, atlanıyor: {channelType}", LogLevel.Warning);
                continue;
            }

            var channel = _factory.GetChannel(channelType);
            request.Channel = channelType;

            try
            {
                context.Log($"OTP kanalı deneniyor: {channelType} (timeout {timeoutSeconds}s)");
                var code = await channel.GetOtpAsync(request, timeout, CancellationToken.None).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(code))
                {
                    throw new TimeoutException("Kanal boş kod döndürdü.");
                }

                request.EncryptedCode = _protector.Encrypt(code);
                request.Status = OtpRequestStatus.Provided;
                request.ProvidedAt = DateTime.UtcNow;
                await _audit.RecordAsync(request, CancellationToken.None).ConfigureAwait(false);

                context.Log($"OTP {channelType} kanalından alındı: {_protector.Mask(code)}");

                return new Dictionary<string, object?>
                {
                    ["otpCode"] = code,
                    ["channelUsed"] = channelType.ToString(),
                    ["otpRequestId"] = request.Id.ToString(),
                    ["encryptedCode"] = request.EncryptedCode
                };
            }
            catch (TimeoutException ex)
            {
                context.Log($"OTP kanalı {channelType} zaman aşımı, sonraki kanala geçiliyor: {ex.Message}", LogLevel.Warning);
            }
            catch (BusinessException ex)
            {
                context.Log($"OTP kanalı {channelType} başarısız, sonraki kanala geçiliyor: {ex.Message}", LogLevel.Warning);
            }
        }

        request.Status = OtpRequestStatus.Failed;
        await _audit.RecordAsync(request, CancellationToken.None).ConfigureAwait(false);
        throw new BusinessException(
            $"OTP alınamadı — denenen tüm kanallar başarısız oldu: {string.Join(", ", channels)}");
    }

    private static List<OtpChannel> ResolveChannels(IActivityExecutionContext context)
    {
        var list = context.GetVariable<List<OtpChannel>>("channels");
        if (list is { Count: > 0 })
        {
            return list;
        }

        var raw = context.GetVariable<string>("channels");
        if (!string.IsNullOrWhiteSpace(raw))
        {
            var parsed = new List<OtpChannel>();
            foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (Enum.TryParse<OtpChannel>(part, ignoreCase: true, out var ch))
                {
                    parsed.Add(ch);
                }
            }

            return parsed;
        }

        return new List<OtpChannel>();
    }
}

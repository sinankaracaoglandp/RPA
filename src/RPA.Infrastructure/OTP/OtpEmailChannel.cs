namespace RPA.Infrastructure.OTP;

using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;

/// <summary>
/// E-posta (IMAP/EWS) kanalı — gelen kutusundaki OTP e-postasından kodu ayıklar (Spec Bölüm 7).
/// Gerçek MailKit/EWS erişimi <see cref="IImapOtpReader"/> arkasında; birim testlerde fixture ile mock'lanır.
/// </summary>
public sealed class OtpEmailChannel : IOtpChannel
{
    private readonly IImapOtpReader _reader;
    private readonly OtpChannelSettings _settings;

    public OtpEmailChannel(IImapOtpReader reader, OtpChannelSettings settings)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public OtpChannel ChannelType => OtpChannel.Email;

    public async Task<string> GetOtpAsync(OtpRequest request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var body = await _reader.FetchLatestMessageBodyAsync(
            _settings.EmailAccount,
            _settings.EmailSenderFilter,
            _settings.EmailSubjectFilter,
            timeout,
            cancellationToken).ConfigureAwait(false);

        var code = OtpCodeExtractor.Extract(body, _settings.CodePattern);
        if (code is null)
        {
            throw new TimeoutException("E-posta kanalı: zaman aşımında uygun OTP kodu bulunamadı.");
        }

        return code;
    }
}

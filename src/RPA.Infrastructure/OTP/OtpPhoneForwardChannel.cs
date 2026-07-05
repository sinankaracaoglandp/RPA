namespace RPA.Infrastructure.OTP;

using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;

/// <summary>
/// Telefon yönlendirme kanalı — yönlendirilen mesaj bir webhook'a POST edilir, oradan kod ayıklanır (Spec Bölüm 7).
/// Gerçek HTTP endpoint <see cref="IWebhookOtpListener"/> arkasında; testlerde mock'lanır.
/// </summary>
public sealed class OtpPhoneForwardChannel : IOtpChannel
{
    private readonly IWebhookOtpListener _listener;
    private readonly OtpChannelSettings _settings;

    public OtpPhoneForwardChannel(IWebhookOtpListener listener, OtpChannelSettings settings)
    {
        _listener = listener ?? throw new ArgumentNullException(nameof(listener));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public OtpChannel ChannelType => OtpChannel.PhoneForward;

    public async Task<string> GetOtpAsync(OtpRequest request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var reference = string.IsNullOrWhiteSpace(_settings.WebhookReference)
            ? request.PortalReference
            : _settings.WebhookReference;

        var payload = await _listener.WaitForForwardedMessageAsync(
            reference,
            timeout,
            cancellationToken).ConfigureAwait(false);

        var code = OtpCodeExtractor.Extract(payload, _settings.CodePattern);
        if (code is null)
        {
            throw new TimeoutException("Telefon yönlendirme kanalı: zaman aşımında yönlendirilmiş mesaj alınmadı.");
        }

        return code;
    }
}

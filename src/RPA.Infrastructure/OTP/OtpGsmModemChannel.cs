namespace RPA.Infrastructure.OTP;

using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;

/// <summary>
/// USB GSM modem kanalı — AT komutlarıyla gelen SMS'ten OTP kodunu ayıklar (Spec Bölüm 7).
/// Gerçek seri port erişimi <see cref="IGsmModemDevice"/> arkasında; testlerde mock'lanır.
/// </summary>
public sealed class OtpGsmModemChannel : IOtpChannel
{
    private readonly IGsmModemDevice _device;
    private readonly OtpChannelSettings _settings;

    public OtpGsmModemChannel(IGsmModemDevice device, OtpChannelSettings settings)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public OtpChannel ChannelType => OtpChannel.GsmModem;

    public async Task<string> GetOtpAsync(OtpRequest request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var sms = await _device.ReadLatestSmsAsync(
            _settings.GsmSenderNumber,
            timeout,
            cancellationToken).ConfigureAwait(false);

        var code = OtpCodeExtractor.Extract(sms, _settings.CodePattern);
        if (code is null)
        {
            throw new TimeoutException("GSM modem kanalı: zaman aşımında OTP içeren SMS alınmadı.");
        }

        return code;
    }
}

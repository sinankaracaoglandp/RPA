namespace RPA.Infrastructure.OTP;

using OtpNet;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;
using BusinessException = RPA.Domain.Exceptions.BusinessException;

/// <summary>
/// TOTP kanalı — Otp.NET ile zaman tabanlı kod üretir (RFC 6238, Spec Bölüm 7).
/// Secret vault'tan çözülüp <see cref="OtpChannelSettings.TotpSecret"/> (Base32) olarak sağlanır.
/// Beklemesiz: geçerli zaman penceresinin kodunu döndürür.
/// </summary>
public sealed class OtpTotpChannel : IOtpChannel
{
    private readonly OtpChannelSettings _settings;

    public OtpTotpChannel(OtpChannelSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public OtpChannel ChannelType => OtpChannel.Totp;

    public Task<string> GetOtpAsync(OtpRequest request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_settings.TotpSecret))
        {
            throw new BusinessException("TOTP kanalı: secret yapılandırılmamış.");
        }

        byte[] secretBytes;
        try
        {
            secretBytes = Base32Encoding.ToBytes(_settings.TotpSecret);
        }
        catch (Exception ex)
        {
            throw new BusinessException($"TOTP kanalı: geçersiz Base32 secret. {ex.Message}");
        }

        var totp = new Totp(secretBytes);
        var code = totp.ComputeTotp();
        return Task.FromResult(code);
    }
}

namespace RPA.Infrastructure.OTP;

using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;

/// <summary>
/// İnsan onaylı kanal — Action Center üzerinden bir kişiden OTP kodu istenir (Spec Bölüm 7).
/// Attended modda dialog, unattended modda ActionItem + bildirim.
/// Gerçek entegrasyon <see cref="IActionCenterClient"/> arkasında; testlerde mock'lanır.
/// </summary>
public sealed class OtpHumanApprovalChannel : IOtpChannel
{
    private readonly IActionCenterClient _client;
    private readonly OtpChannelSettings _settings;

    public OtpHumanApprovalChannel(IActionCenterClient client, OtpChannelSettings settings)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public OtpChannel ChannelType => OtpChannel.HumanApproval;

    public async Task<string> GetOtpAsync(OtpRequest request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var response = await _client.RequestOtpFromHumanAsync(
            request,
            timeout,
            cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(response))
        {
            throw new TimeoutException("İnsan onaylı kanal: zaman aşımında yanıt alınmadı.");
        }

        // İnsan doğrudan kodu girer; yine de desen doğrulaması yaparak temiz kodu döndürürüz.
        var code = OtpCodeExtractor.Extract(response, _settings.CodePattern) ?? response.Trim();
        return code;
    }
}

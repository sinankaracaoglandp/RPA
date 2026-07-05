namespace RPA.Infrastructure.OTP;

using RPA.Domain.Enums;
using RPA.Domain.Interfaces;
using BusinessException = RPA.Domain.Exceptions.BusinessException;

/// <summary>
/// <see cref="OtpChannel"/> türüne göre ilgili <see cref="IOtpChannel"/> implementasyonunu döndürür (Spec Bölüm 7).
/// DI ile kayıtlı tüm kanallar constructor'a enjekte edilir; tür → implementasyon eşlenir.
/// </summary>
public interface IOtpChannelFactory
{
    /// <summary>Verilen tür için kanalı döndürür. Kayıtlı değilse <see cref="BusinessException"/>.</summary>
    IOtpChannel GetChannel(OtpChannel channelType);

    /// <summary>Bir türün desteklenip desteklenmediği.</summary>
    bool Supports(OtpChannel channelType);
}

/// <inheritdoc />
public sealed class OtpChannelFactory : IOtpChannelFactory
{
    private readonly IReadOnlyDictionary<OtpChannel, IOtpChannel> _channels;

    /// <summary>
    /// DI, kayıtlı tüm <see cref="IOtpChannel"/> örneklerini enjekte eder.
    /// Aynı tür birden fazla kayıtlıysa son kayıt kazanır.
    /// </summary>
    public OtpChannelFactory(IEnumerable<IOtpChannel> channels)
    {
        ArgumentNullException.ThrowIfNull(channels);

        var map = new Dictionary<OtpChannel, IOtpChannel>();
        foreach (var channel in channels)
        {
            map[channel.ChannelType] = channel;
        }

        _channels = map;
    }

    public bool Supports(OtpChannel channelType) => _channels.ContainsKey(channelType);

    public IOtpChannel GetChannel(OtpChannel channelType)
    {
        if (_channels.TryGetValue(channelType, out var channel))
        {
            return channel;
        }

        throw new BusinessException($"OTP kanalı kayıtlı değil: {channelType}");
    }
}

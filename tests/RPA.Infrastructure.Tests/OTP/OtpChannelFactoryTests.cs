namespace RPA.Infrastructure.Tests.OTP;

using Moq;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.OTP;
using BusinessException = RPA.Domain.Exceptions.BusinessException;

/// <summary>
/// <see cref="OtpChannelFactory"/> testleri (Task 4.3, Spec Bölüm 7).
/// Tür → implementasyon yönlendirmesi ve 5 kanalın çözümlenmesi.
/// </summary>
public class OtpChannelFactoryTests
{
    private static IOtpChannel FakeChannel(OtpChannel type)
    {
        var mock = new Mock<IOtpChannel>();
        mock.SetupGet(c => c.ChannelType).Returns(type);
        return mock.Object;
    }

    [Fact]
    public void GetChannel_RoutesByChannelType()
    {
        var email = FakeChannel(OtpChannel.Email);
        var totp = FakeChannel(OtpChannel.Totp);
        var factory = new OtpChannelFactory(new[] { email, totp });

        Assert.Same(email, factory.GetChannel(OtpChannel.Email));
        Assert.Same(totp, factory.GetChannel(OtpChannel.Totp));
        Assert.True(factory.Supports(OtpChannel.Email));
        Assert.False(factory.Supports(OtpChannel.GsmModem));
    }

    [Fact]
    public void GetChannel_AllFiveChannelTypes_Resolve()
    {
        var channels = new[]
        {
            FakeChannel(OtpChannel.Email),
            FakeChannel(OtpChannel.Totp),
            FakeChannel(OtpChannel.GsmModem),
            FakeChannel(OtpChannel.PhoneForward),
            FakeChannel(OtpChannel.HumanApproval)
        };
        var factory = new OtpChannelFactory(channels);

        foreach (var type in Enum.GetValues<OtpChannel>())
        {
            Assert.True(factory.Supports(type));
            Assert.Equal(type, factory.GetChannel(type).ChannelType);
        }
    }

    [Fact]
    public void GetChannel_Unregistered_ThrowsBusinessException()
    {
        var factory = new OtpChannelFactory(Array.Empty<IOtpChannel>());

        Assert.Throws<BusinessException>(() => factory.GetChannel(OtpChannel.Email));
    }
}

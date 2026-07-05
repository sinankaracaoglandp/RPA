namespace RPA.Infrastructure.Tests.Activities;

using Moq;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.OTP;
using BusinessException = RPA.Domain.Exceptions.BusinessException;

/// <summary>
/// <see cref="GetOtpActivity"/> testleri (Task 4.3, Spec Bölüm 7).
/// Tek kanal başarı/zaman aşımı, sıralı fallback zinciri, tüm kanal başarısızlığı
/// (BusinessException) ve aktivite metadata doğrulaması.
/// </summary>
public class GetOtpActivityTests
{
    /// <summary>Yapılandırılabilir fake kanal — kod döner veya TimeoutException fırlatır.</summary>
    private sealed class FakeChannel : IOtpChannel
    {
        private readonly Func<string> _behavior;
        public FakeChannel(OtpChannel type, Func<string> behavior)
        {
            ChannelType = type;
            _behavior = behavior;
        }

        public OtpChannel ChannelType { get; }
        public int Calls { get; private set; }

        public Task<string> GetOtpAsync(OtpRequest request, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_behavior());
        }
    }

    private static (Mock<IOtpCodeProtector> protector, Mock<IOtpAuditSink> audit) Deps()
    {
        var protector = new Mock<IOtpCodeProtector>();
        protector.Setup(p => p.Encrypt(It.IsAny<string>())).Returns<string>(c => "ENC(" + c + ")");
        protector.Setup(p => p.Mask(It.IsAny<string>())).Returns("[MASKED]");
        var audit = new Mock<IOtpAuditSink>();
        audit.Setup(a => a.RecordAsync(It.IsAny<OtpRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return (protector, audit);
    }

    private static GetOtpActivity Activity(IEnumerable<IOtpChannel> channels,
        Mock<IOtpCodeProtector> protector, Mock<IOtpAuditSink> audit)
        => new(new OtpChannelFactory(channels), protector.Object, audit.Object);

    private static TestActivityExecutionContext Context(List<OtpChannel> channels, string portal = "https://portal")
    {
        var ctx = new TestActivityExecutionContext();
        ctx.SetVariable("portalReference", portal);
        ctx.SetVariable("channels", channels);
        ctx.SetVariable("timeoutSeconds", 5);
        return ctx;
    }

    [Fact]
    public async Task SingleChannel_Success_ReturnsCodeAndAudits()
    {
        var (protector, audit) = Deps();
        var ch = new FakeChannel(OtpChannel.Totp, () => "123456");
        var activity = Activity(new[] { ch }, protector, audit);

        var result = await activity.ExecuteAsync(Context(new() { OtpChannel.Totp }));

        Assert.Equal("123456", result["otpCode"]);
        Assert.Equal("Totp", result["channelUsed"]);
        Assert.Equal("ENC(123456)", result["encryptedCode"]);
        Assert.NotNull(result["otpRequestId"]);
        protector.Verify(p => p.Encrypt("123456"), Times.Once);
        audit.Verify(a => a.RecordAsync(
            It.Is<OtpRequest>(r => r.Status == OtpRequestStatus.Provided), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SingleChannel_Timeout_ThrowsBusinessExceptionAndAuditsFailed()
    {
        var (protector, audit) = Deps();
        var ch = new FakeChannel(OtpChannel.Email, () => throw new TimeoutException("no mail"));
        var activity = Activity(new[] { ch }, protector, audit);

        await Assert.ThrowsAsync<BusinessException>(
            () => activity.ExecuteAsync(Context(new() { OtpChannel.Email })));

        audit.Verify(a => a.RecordAsync(
            It.Is<OtpRequest>(r => r.Status == OtpRequestStatus.Failed), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FallbackChain_FirstTimeout_SecondSucceeds()
    {
        var (protector, audit) = Deps();
        var ch1 = new FakeChannel(OtpChannel.Email, () => throw new TimeoutException("timeout"));
        var ch2 = new FakeChannel(OtpChannel.Totp, () => "654321");
        var activity = Activity(new IOtpChannel[] { ch1, ch2 }, protector, audit);

        var result = await activity.ExecuteAsync(
            Context(new() { OtpChannel.Email, OtpChannel.Totp }));

        Assert.Equal("654321", result["otpCode"]);
        Assert.Equal("Totp", result["channelUsed"]);
        Assert.Equal(1, ch1.Calls);
        Assert.Equal(1, ch2.Calls);
    }

    [Fact]
    public async Task FallbackChain_BusinessExceptionFallsThrough()
    {
        var (protector, audit) = Deps();
        var ch1 = new FakeChannel(OtpChannel.Totp,
            () => throw new BusinessException("secret yok"));
        var ch2 = new FakeChannel(OtpChannel.HumanApproval, () => "111222");
        var activity = Activity(new IOtpChannel[] { ch1, ch2 }, protector, audit);

        var result = await activity.ExecuteAsync(
            Context(new() { OtpChannel.Totp, OtpChannel.HumanApproval }));

        Assert.Equal("111222", result["otpCode"]);
        Assert.Equal("HumanApproval", result["channelUsed"]);
    }

    [Fact]
    public async Task AllChannelsTimeout_ThrowsBusinessException()
    {
        var (protector, audit) = Deps();
        var ch1 = new FakeChannel(OtpChannel.Email, () => throw new TimeoutException());
        var ch2 = new FakeChannel(OtpChannel.GsmModem, () => throw new TimeoutException());
        var activity = Activity(new IOtpChannel[] { ch1, ch2 }, protector, audit);

        await Assert.ThrowsAsync<BusinessException>(
            () => activity.ExecuteAsync(Context(new() { OtpChannel.Email, OtpChannel.GsmModem })));

        Assert.Equal(1, ch1.Calls);
        Assert.Equal(1, ch2.Calls);
    }

    [Fact]
    public async Task EmptyPortalReference_ThrowsBusinessException()
    {
        var (protector, audit) = Deps();
        var activity = Activity(Array.Empty<IOtpChannel>(), protector, audit);

        await Assert.ThrowsAsync<BusinessException>(
            () => activity.ExecuteAsync(Context(new() { OtpChannel.Email }, portal: "")));
    }

    [Fact]
    public async Task NoChannels_ThrowsBusinessException()
    {
        var (protector, audit) = Deps();
        var activity = Activity(Array.Empty<IOtpChannel>(), protector, audit);
        var ctx = new TestActivityExecutionContext();
        ctx.SetVariable("portalReference", "https://portal");
        // "channels" hiç set edilmez → çözümlenen kanal listesi boş.

        await Assert.ThrowsAsync<BusinessException>(() => activity.ExecuteAsync(ctx));
    }

    [Fact]
    public async Task UnregisteredChannel_SkippedThenFallsToRegistered()
    {
        var (protector, audit) = Deps();
        // Yalnızca Totp kayıtlı; Email istenir ama kayıtlı değil → atlanır.
        var totp = new FakeChannel(OtpChannel.Totp, () => "777888");
        var activity = Activity(new[] { totp }, protector, audit);

        var result = await activity.ExecuteAsync(
            Context(new() { OtpChannel.Email, OtpChannel.Totp }));

        Assert.Equal("777888", result["otpCode"]);
        Assert.Equal("Totp", result["channelUsed"]);
    }

    [Fact]
    public void Metadata_IsValid()
    {
        var (protector, audit) = Deps();
        var activity = Activity(Array.Empty<IOtpChannel>(), protector, audit);

        var meta = activity.GetMetadata();

        Assert.Equal("Otp.Get", meta.ActivityId);
        Assert.Equal("Güvenlik", meta.Category);
        Assert.Contains("otp", meta.RequiredCapabilities);
        Assert.Contains(meta.Inputs, p => p.Name == "portalReference" && p.Required);
        Assert.Contains(meta.Inputs, p => p.Name == "channels" && p.Required);
        Assert.Contains(meta.Outputs, p => p.Name == "otpCode");
        Assert.NotNull(meta.ExceptionClassification);
        Assert.Equal(ExceptionType.Business, meta.ExceptionClassification!.Classification);
    }
}

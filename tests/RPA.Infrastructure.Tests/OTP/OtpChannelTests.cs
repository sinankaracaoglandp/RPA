namespace RPA.Infrastructure.Tests.OTP;

using Moq;
using OtpNet;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.OTP;
using BusinessException = RPA.Domain.Exceptions.BusinessException;

/// <summary>
/// 5 OTP kanalının birim testleri (Task 4.3, Spec Bölüm 7).
/// Her kanalın dış bağımlılığı (IMAP, GSM modem, webhook, Action Center) mock'lanır;
/// başarı ve zaman aşımı yolları doğrulanır. Arrange-Act-Assert deseni.
/// </summary>
public class OtpChannelTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private static OtpRequest NewRequest() => new()
    {
        JobRunId = Guid.NewGuid(),
        PortalReference = "https://portal.example.com",
        Status = OtpRequestStatus.Pending,
        ExpiresAt = DateTime.UtcNow.AddMinutes(5)
    };

    // =============================================================== Email

    [Fact]
    public async Task EmailChannel_Success_ExtractsCode()
    {
        var reader = new Mock<IImapOtpReader>();
        reader.Setup(r => r.FetchLatestMessageBodyAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Giriş kodunuz: 123456. 5 dk içinde kullanın.");

        var channel = new OtpEmailChannel(reader.Object, new OtpChannelSettings());

        var code = await channel.GetOtpAsync(NewRequest(), Timeout, CancellationToken.None);

        Assert.Equal("123456", code);
        Assert.Equal(OtpChannel.Email, channel.ChannelType);
    }

    [Fact]
    public async Task EmailChannel_Timeout_ThrowsTimeoutException()
    {
        var reader = new Mock<IImapOtpReader>();
        reader.Setup(r => r.FetchLatestMessageBodyAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var channel = new OtpEmailChannel(reader.Object, new OtpChannelSettings());

        await Assert.ThrowsAsync<TimeoutException>(
            () => channel.GetOtpAsync(NewRequest(), Timeout, CancellationToken.None));
    }

    // =============================================================== TOTP

    [Fact]
    public async Task TotpChannel_Success_GeneratesCorrectCode()
    {
        // Base32 secret; kanalın ürettiği kod Otp.NET referans hesabıyla aynı olmalı.
        var secret = Base32Encoding.ToString(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
        var settings = new OtpChannelSettings { TotpSecret = secret };
        var channel = new OtpTotpChannel(settings);

        var code = await channel.GetOtpAsync(NewRequest(), Timeout, CancellationToken.None);

        var expected = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp();
        Assert.Equal(expected, code);
        Assert.Matches(@"^\d{6}$", code);
        Assert.Equal(OtpChannel.Totp, channel.ChannelType);
    }

    [Fact]
    public async Task TotpChannel_MissingSecret_ThrowsBusinessException()
    {
        var channel = new OtpTotpChannel(new OtpChannelSettings { TotpSecret = null });

        await Assert.ThrowsAsync<BusinessException>(
            () => channel.GetOtpAsync(NewRequest(), Timeout, CancellationToken.None));
    }

    // =============================================================== GSM modem

    [Fact]
    public async Task GsmModemChannel_Success_ExtractsCode()
    {
        var device = new Mock<IGsmModemDevice>();
        device.Setup(d => d.ReadLatestSmsAsync(
                It.IsAny<string?>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Your code is 654321");

        var channel = new OtpGsmModemChannel(device.Object, new OtpChannelSettings());

        var code = await channel.GetOtpAsync(NewRequest(), Timeout, CancellationToken.None);

        Assert.Equal("654321", code);
        Assert.Equal(OtpChannel.GsmModem, channel.ChannelType);
    }

    [Fact]
    public async Task GsmModemChannel_Timeout_ThrowsTimeoutException()
    {
        var device = new Mock<IGsmModemDevice>();
        device.Setup(d => d.ReadLatestSmsAsync(
                It.IsAny<string?>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var channel = new OtpGsmModemChannel(device.Object, new OtpChannelSettings());

        await Assert.ThrowsAsync<TimeoutException>(
            () => channel.GetOtpAsync(NewRequest(), Timeout, CancellationToken.None));
    }

    // =============================================================== Phone forward

    [Fact]
    public async Task PhoneForwardChannel_Success_ExtractsCode()
    {
        var listener = new Mock<IWebhookOtpListener>();
        listener.Setup(l => l.WaitForForwardedMessageAsync(
                It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Forwarded SMS: 246810 is your OTP");

        var channel = new OtpPhoneForwardChannel(listener.Object, new OtpChannelSettings());

        var code = await channel.GetOtpAsync(NewRequest(), Timeout, CancellationToken.None);

        Assert.Equal("246810", code);
        Assert.Equal(OtpChannel.PhoneForward, channel.ChannelType);
    }

    [Fact]
    public async Task PhoneForwardChannel_UsesPortalReference_WhenNoWebhookReference()
    {
        var listener = new Mock<IWebhookOtpListener>();
        listener.Setup(l => l.WaitForForwardedMessageAsync(
                It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("112233");

        var channel = new OtpPhoneForwardChannel(listener.Object, new OtpChannelSettings());
        var request = NewRequest();

        await channel.GetOtpAsync(request, Timeout, CancellationToken.None);

        listener.Verify(l => l.WaitForForwardedMessageAsync(
            request.PortalReference, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PhoneForwardChannel_Timeout_ThrowsTimeoutException()
    {
        var listener = new Mock<IWebhookOtpListener>();
        listener.Setup(l => l.WaitForForwardedMessageAsync(
                It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var channel = new OtpPhoneForwardChannel(listener.Object, new OtpChannelSettings());

        await Assert.ThrowsAsync<TimeoutException>(
            () => channel.GetOtpAsync(NewRequest(), Timeout, CancellationToken.None));
    }

    // =============================================================== Human approval

    [Fact]
    public async Task HumanApprovalChannel_Success_ReturnsUserResponse()
    {
        var client = new Mock<IActionCenterClient>();
        client.Setup(c => c.RequestOtpFromHumanAsync(
                It.IsAny<OtpRequest>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("998877");

        var channel = new OtpHumanApprovalChannel(client.Object, new OtpChannelSettings());

        var code = await channel.GetOtpAsync(NewRequest(), Timeout, CancellationToken.None);

        Assert.Equal("998877", code);
        Assert.Equal(OtpChannel.HumanApproval, channel.ChannelType);
    }

    [Fact]
    public async Task HumanApprovalChannel_Timeout_ThrowsTimeoutException()
    {
        var client = new Mock<IActionCenterClient>();
        client.Setup(c => c.RequestOtpFromHumanAsync(
                It.IsAny<OtpRequest>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var channel = new OtpHumanApprovalChannel(client.Object, new OtpChannelSettings());

        await Assert.ThrowsAsync<TimeoutException>(
            () => channel.GetOtpAsync(NewRequest(), Timeout, CancellationToken.None));
    }

    // =============================================================== OtpRequest entity

    [Fact]
    public void OtpRequest_Creation_SetsAllFields()
    {
        var jobRunId = Guid.NewGuid();
        var expires = DateTime.UtcNow.AddMinutes(5);
        var request = new OtpRequest
        {
            JobRunId = jobRunId,
            Channel = OtpChannel.Email,
            PortalReference = "https://portal.example.com",
            EncryptedCode = "cipher==",
            Status = OtpRequestStatus.Pending,
            ExpiresAt = expires
        };

        Assert.Equal(jobRunId, request.JobRunId);
        Assert.Equal(OtpChannel.Email, request.Channel);
        Assert.Equal("https://portal.example.com", request.PortalReference);
        Assert.Equal("cipher==", request.EncryptedCode);
        Assert.Equal(OtpRequestStatus.Pending, request.Status);
        Assert.Equal(expires, request.ExpiresAt);
        Assert.Null(request.ProvidedAt);
        Assert.NotEqual(Guid.Empty, request.Id);
    }

    [Theory]
    [InlineData(OtpRequestStatus.Provided)]
    [InlineData(OtpRequestStatus.Expired)]
    [InlineData(OtpRequestStatus.Failed)]
    public void OtpRequest_StatusTransition_FromPending(OtpRequestStatus target)
    {
        var request = NewRequest();
        Assert.Equal(OtpRequestStatus.Pending, request.Status);

        request.Status = target;
        if (target == OtpRequestStatus.Provided)
        {
            request.ProvidedAt = DateTime.UtcNow;
            Assert.NotNull(request.ProvidedAt);
        }

        Assert.Equal(target, request.Status);
    }

    [Fact]
    public async Task HumanApprovalChannel_ExtractsCodeFromNoisyResponse()
    {
        var client = new Mock<IActionCenterClient>();
        client.Setup(c => c.RequestOtpFromHumanAsync(
                It.IsAny<OtpRequest>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("kod 445566 tesekkurler");

        var channel = new OtpHumanApprovalChannel(client.Object, new OtpChannelSettings());

        var code = await channel.GetOtpAsync(NewRequest(), Timeout, CancellationToken.None);

        Assert.Equal("445566", code);
    }

    [Fact]
    public async Task EmailChannel_CustomPattern_ExtractsFourDigits()
    {
        var reader = new Mock<IImapOtpReader>();
        reader.Setup(r => r.FetchLatestMessageBodyAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("PIN: 4821");

        var channel = new OtpEmailChannel(reader.Object, new OtpChannelSettings { CodePattern = @"\d{4}" });

        var code = await channel.GetOtpAsync(NewRequest(), Timeout, CancellationToken.None);

        Assert.Equal("4821", code);
    }
}

/// <summary>OtpCodeExtractor birim testleri.</summary>
public class OtpCodeExtractorTests
{
    [Theory]
    [InlineData("Kodunuz: 123456", @"\d{6}", "123456")]
    [InlineData("PIN 4821 girin", @"\d{4}", "4821")]
    [InlineData("no digits here", @"\d{6}", null)]
    [InlineData("", @"\d{6}", null)]
    [InlineData(null, @"\d{6}", null)]
    public void Extract_ReturnsExpected(string? body, string pattern, string? expected)
    {
        Assert.Equal(expected, OtpCodeExtractor.Extract(body, pattern));
    }

    [Fact]
    public void Extract_EmptyPattern_FallsBackToSixDigits()
    {
        Assert.Equal("987654", OtpCodeExtractor.Extract("code 987654", ""));
    }
}

/// <summary>GetOtpActivity çok kanallı fallback orkestrasyonu testleri (Spec Bölüm 7).</summary>
public class GetOtpActivityTests
{
    private sealed class FakeCtx : IActivityExecutionContext
    {
        private readonly Dictionary<string, object?> _vars;
        public FakeCtx(Dictionary<string, object?> vars) => _vars = vars;
        public T GetVariable<T>(string name)
        {
            if (_vars.TryGetValue(name, out var v) && v is T t) return t;
            return default!;
        }
        public void SetVariable(string name, object? value) => _vars[name] = value;
        public Task<string> GetCredentialAsync(string name) => Task.FromResult("");
        public Task<string?> GetAssetAsync(string name) => Task.FromResult<string?>(null);
        public void Log(string message, LogLevel level = LogLevel.Information) { }
        public string TimeZone => "UTC";
        public Guid JobRunId { get; } = Guid.NewGuid();
    }

    private static GetOtpActivity Build(
        out Mock<IOtpCodeProtector> protector,
        out Mock<IOtpAuditSink> audit,
        params IOtpChannel[] channels)
    {
        protector = new Mock<IOtpCodeProtector>();
        protector.Setup(p => p.Encrypt(It.IsAny<string>())).Returns<string>(c => "enc:" + c);
        protector.Setup(p => p.Mask(It.IsAny<string>())).Returns("[MASKED]");
        audit = new Mock<IOtpAuditSink>();
        audit.Setup(a => a.RecordAsync(It.IsAny<OtpRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var factory = new OtpChannelFactory(channels);
        return new GetOtpActivity(factory, protector.Object, audit.Object);
    }

    private static IOtpChannel Succeeds(OtpChannel type, string code)
    {
        var m = new Mock<IOtpChannel>();
        m.SetupGet(c => c.ChannelType).Returns(type);
        m.Setup(c => c.GetOtpAsync(It.IsAny<OtpRequest>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(code);
        return m.Object;
    }

    private static IOtpChannel TimesOut(OtpChannel type)
    {
        var m = new Mock<IOtpChannel>();
        m.SetupGet(c => c.ChannelType).Returns(type);
        m.Setup(c => c.GetOtpAsync(It.IsAny<OtpRequest>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException());
        return m.Object;
    }

    [Fact]
    public async Task Execute_FirstChannelSucceeds_ReturnsCode()
    {
        var activity = Build(out var protector, out var audit, Succeeds(OtpChannel.Email, "123456"));
        var ctx = new FakeCtx(new()
        {
            ["portalReference"] = "https://p",
            ["channels"] = new List<OtpChannel> { OtpChannel.Email }
        });

        var outputs = await activity.ExecuteAsync(ctx);

        Assert.Equal("123456", outputs["otpCode"]);
        Assert.Equal("Email", outputs["channelUsed"]);
        Assert.Equal("enc:123456", outputs["encryptedCode"]);
        protector.Verify(p => p.Encrypt("123456"), Times.Once);
        audit.Verify(a => a.RecordAsync(
            It.Is<OtpRequest>(r => r.Status == OtpRequestStatus.Provided && r.ProvidedAt != null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_FirstTimesOut_FallsBackToSecond()
    {
        var activity = Build(out _, out _,
            TimesOut(OtpChannel.Email),
            Succeeds(OtpChannel.GsmModem, "654321"));
        var ctx = new FakeCtx(new()
        {
            ["portalReference"] = "https://p",
            ["channels"] = new List<OtpChannel> { OtpChannel.Email, OtpChannel.GsmModem }
        });

        var outputs = await activity.ExecuteAsync(ctx);

        Assert.Equal("654321", outputs["otpCode"]);
        Assert.Equal("GsmModem", outputs["channelUsed"]);
    }

    [Fact]
    public async Task Execute_AllChannelsFail_ThrowsBusinessAndRecordsFailed()
    {
        var activity = Build(out _, out var audit,
            TimesOut(OtpChannel.Email),
            TimesOut(OtpChannel.GsmModem));
        var ctx = new FakeCtx(new()
        {
            ["portalReference"] = "https://p",
            ["channels"] = new List<OtpChannel> { OtpChannel.Email, OtpChannel.GsmModem }
        });

        await Assert.ThrowsAsync<BusinessException>(() => activity.ExecuteAsync(ctx));
        audit.Verify(a => a.RecordAsync(
            It.Is<OtpRequest>(r => r.Status == OtpRequestStatus.Failed),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_MissingPortalReference_ThrowsBusiness()
    {
        var activity = Build(out _, out _, Succeeds(OtpChannel.Email, "1"));
        var ctx = new FakeCtx(new() { ["channels"] = new List<OtpChannel> { OtpChannel.Email } });

        await Assert.ThrowsAsync<BusinessException>(() => activity.ExecuteAsync(ctx));
    }

    [Fact]
    public async Task Execute_NoChannels_ThrowsBusiness()
    {
        var activity = Build(out _, out _, Succeeds(OtpChannel.Email, "1"));
        var ctx = new FakeCtx(new() { ["portalReference"] = "https://p" });

        await Assert.ThrowsAsync<BusinessException>(() => activity.ExecuteAsync(ctx));
    }

    [Fact]
    public async Task Execute_ChannelsFromCommaString_Parsed()
    {
        var activity = Build(out _, out _, Succeeds(OtpChannel.Totp, "222333"));
        var ctx = new FakeCtx(new()
        {
            ["portalReference"] = "https://p",
            ["channels"] = "Totp"
        });

        var outputs = await activity.ExecuteAsync(ctx);
        Assert.Equal("222333", outputs["otpCode"]);
    }

    [Fact]
    public async Task Execute_UnregisteredChannelSkipped_ThenNextSucceeds()
    {
        // Yalnızca GsmModem kayıtlı; istenen ilk kanal (Email) kayıtlı değil → atlanır.
        var activity = Build(out _, out _, Succeeds(OtpChannel.GsmModem, "777888"));
        var ctx = new FakeCtx(new()
        {
            ["portalReference"] = "https://p",
            ["channels"] = new List<OtpChannel> { OtpChannel.Email, OtpChannel.GsmModem }
        });

        var outputs = await activity.ExecuteAsync(ctx);
        Assert.Equal("GsmModem", outputs["channelUsed"]);
    }

    [Fact]
    public void Metadata_HasExpectedId()
    {
        var activity = Build(out _, out _, Succeeds(OtpChannel.Email, "1"));
        Assert.Equal("Otp.Get", activity.GetMetadata().ActivityId);
    }
}

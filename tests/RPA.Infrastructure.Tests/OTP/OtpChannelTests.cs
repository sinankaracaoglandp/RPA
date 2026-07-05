namespace RPA.Infrastructure.Tests.OTP;

using Moq;
using OtpNet;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
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
}

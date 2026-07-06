namespace RPA.Infrastructure.Tests;

using System.Net;
using RPA.Infrastructure.Alerting;

/// <summary>WP-6.3 — ChannelNotificationSender: teams webhook HTTP POST + email seam yönlendirmesi.</summary>
public class ChannelNotificationSenderTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public readonly List<(Uri? url, string body)> Requests = new();
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri, body));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class FakeEmail : IAlertEmailSender
    {
        public int Calls;
        public string? LastRecipients;
        public Task SendAsync(string recipients, string subject, string body, CancellationToken ct = default)
        {
            Calls++;
            LastRecipients = recipients;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Teams_PostsToEachWebhookUrl()
    {
        var handler = new CapturingHandler();
        var http = new HttpClient(handler);
        var email = new FakeEmail();
        var sender = new ChannelNotificationSender(http, email);

        await sender.SendAsync("teams", "https://team1.example/hook, https://team2.example/hook", "alarm!");

        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("alarm!", handler.Requests[0].body);
        Assert.Equal(0, email.Calls);
    }

    [Fact]
    public async Task Email_DelegatesToEmailSender()
    {
        var sender = new ChannelNotificationSender(new HttpClient(new CapturingHandler()), new FakeEmail());
        var email = new FakeEmail();
        var s2 = new ChannelNotificationSender(new HttpClient(new CapturingHandler()), email);

        await s2.SendAsync("email", "ops@example.com", "alarm!");

        Assert.Equal(1, email.Calls);
        Assert.Equal("ops@example.com", email.LastRecipients);
    }

    [Fact]
    public async Task UnknownChannel_IsIgnored()
    {
        var handler = new CapturingHandler();
        var email = new FakeEmail();
        var sender = new ChannelNotificationSender(new HttpClient(handler), email);

        await sender.SendAsync("sms", "x", "alarm!");

        Assert.Empty(handler.Requests);
        Assert.Equal(0, email.Calls);
    }
}

namespace RPA.WebAPI.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;

/// <summary>
/// QueuesController HTTP-seviyesi testleri (Task 3.2). IQueueService stub'lanır.
/// </summary>
public class QueuesControllerIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public QueuesControllerIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient(IQueueService service)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IQueueService));
                if (descriptor is not null) services.Remove(descriptor);
                services.AddScoped(_ => service);
                services.AddAuthentication("TestAuth")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("TestAuth", _ => { });
            });
        }).CreateClient();
    }

    [Fact]
    public async Task NextItem_ReturnsClaimedItem()
    {
        var queueId = Guid.NewGuid();
        var robotId = Guid.NewGuid();
        var item = new QueueItem { QueueId = queueId, Status = QueueItemStatus.InProgress, AssignedRobotId = robotId };
        var mock = new Mock<IQueueService>();
        mock.Setup(s => s.GetNextItemAsync(queueId, robotId, It.IsAny<CancellationToken>())).ReturnsAsync(item);
        var client = CreateClient(mock.Object);

        var response = await client.GetAsync($"/api/queues/{queueId}/nextitem?robotId={robotId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<QueueItemDto>();
        Assert.Equal("InProgress", body!.Status);
        Assert.Equal(robotId, body.AssignedRobotId);
    }

    [Fact]
    public async Task NextItem_EmptyQueue_Returns204()
    {
        var mock = new Mock<IQueueService>();
        mock.Setup(s => s.GetNextItemAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueueItem?)null);
        var client = CreateClient(mock.Object);

        var response = await client.GetAsync($"/api/queues/{Guid.NewGuid()}/nextitem?robotId={Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task NextItem_MissingRobotId_Returns400()
    {
        var client = CreateClient(new Mock<IQueueService>().Object);
        var response = await client.GetAsync($"/api/queues/{Guid.NewGuid()}/nextitem?robotId={Guid.Empty}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetItem_ReturnsItem_WhenItBelongsToQueue()
    {
        var queueId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var mock = new Mock<IQueueService>();
        mock.Setup(s => s.GetItemAsync(itemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueueItem { Id = itemId, QueueId = queueId, Status = QueueItemStatus.Successful });
        var client = CreateClient(mock.Object);

        var response = await client.GetAsync($"/api/queues/{queueId}/items/{itemId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<QueueItemDto>();
        Assert.Equal(itemId, body!.Id);
        Assert.Equal("Successful", body.Status);
    }

    [Fact]
    public async Task GetItem_Returns404_WhenItemBelongsToAnotherQueue()
    {
        var itemId = Guid.NewGuid();
        var mock = new Mock<IQueueService>();
        mock.Setup(s => s.GetItemAsync(itemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueueItem { Id = itemId, QueueId = Guid.NewGuid(), Status = QueueItemStatus.New });
        var client = CreateClient(mock.Object);

        var response = await client.GetAsync($"/api/queues/{Guid.NewGuid()}/items/{itemId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateItem_Successful_CallsComplete()
    {
        var itemId = Guid.NewGuid();
        var mock = new Mock<IQueueService>();
        mock.Setup(s => s.CompleteAsync(itemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueueItem { Id = itemId, Status = QueueItemStatus.Successful });
        var client = CreateClient(mock.Object);

        var response = await client.PatchAsJsonAsync(
            $"/api/queues/{Guid.NewGuid()}/items/{itemId}", new { status = "Successful" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        mock.Verify(s => s.CompleteAsync(itemId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateItem_Failed_CallsFailWithSystemException()
    {
        var itemId = Guid.NewGuid();
        var mock = new Mock<IQueueService>();
        mock.Setup(s => s.FailAsync(itemId, It.IsAny<string?>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueueItem { Id = itemId, Status = QueueItemStatus.New });
        var client = CreateClient(mock.Object);

        var response = await client.PatchAsJsonAsync(
            $"/api/queues/{Guid.NewGuid()}/items/{itemId}", new { status = "Failed", errorDetail = "timeout" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        mock.Verify(s => s.FailAsync(itemId, "timeout", false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateItem_BusinessException_CallsFailWithBusinessFlag()
    {
        var itemId = Guid.NewGuid();
        var mock = new Mock<IQueueService>();
        mock.Setup(s => s.FailAsync(itemId, It.IsAny<string?>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueueItem { Id = itemId, Status = QueueItemStatus.BusinessException });
        var client = CreateClient(mock.Object);

        var response = await client.PatchAsJsonAsync(
            $"/api/queues/{Guid.NewGuid()}/items/{itemId}", new { status = "BusinessException", errorDetail = "kural" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        mock.Verify(s => s.FailAsync(itemId, "kural", true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateItem_UnknownItem_Returns404()
    {
        var mock = new Mock<IQueueService>();
        mock.Setup(s => s.CompleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueueItem?)null);
        var client = CreateClient(mock.Object);

        var response = await client.PatchAsJsonAsync(
            $"/api/queues/{Guid.NewGuid()}/items/{Guid.NewGuid()}", new { status = "Successful" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateItem_InvalidStatus_Returns400()
    {
        var client = CreateClient(new Mock<IQueueService>().Object);
        var response = await client.PatchAsJsonAsync(
            $"/api/queues/{Guid.NewGuid()}/items/{Guid.NewGuid()}", new { status = "Bogus" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private class QueueItemDto
    {
        public Guid Id { get; set; }
        public string Status { get; set; } = string.Empty;
        public Guid? AssignedRobotId { get; set; }
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, "test-user") },
                Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}

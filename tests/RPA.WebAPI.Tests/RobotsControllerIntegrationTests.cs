namespace RPA.WebAPI.Tests;

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;

/// <summary>
/// RobotsController HTTP-seviyesi testleri (Task 3.1). IRobotService stub'lanır.
/// </summary>
public class RobotsControllerIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RobotsControllerIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient(IRobotService robotService)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IRobotService));
                if (descriptor is not null) services.Remove(descriptor);
                services.AddScoped(_ => robotService);
            });
        }).CreateClient();
    }

    [Fact]
    public async Task Register_ReturnsCreatedRobot()
    {
        var mock = new Mock<IRobotService>();
        var robot = new Robot { MachineName = "RPA-01", Status = RobotStatus.Online, LastHeartbeat = DateTime.UtcNow };
        mock.Setup(s => s.RegisterAsync(It.IsAny<RobotRegistrationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(robot);
        var client = CreateClient(mock.Object);

        var response = await client.PostAsJsonAsync("/api/robots/register",
            new { machineName = "RPA-01", mode = "Unattended" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RobotDto>();
        Assert.Equal("RPA-01", body!.MachineName);
    }

    [Fact]
    public async Task Register_EmptyMachineName_Returns400()
    {
        var client = CreateClient(new Mock<IRobotService>().Object);
        var response = await client.PostAsJsonAsync("/api/robots/register", new { machineName = "" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_Existing_Returns200()
    {
        var id = Guid.NewGuid();
        var mock = new Mock<IRobotService>();
        mock.Setup(s => s.GetAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Robot { Id = id, MachineName = "RPA-01" });
        var client = CreateClient(mock.Object);

        var response = await client.GetAsync($"/api/robots/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_Unknown_Returns404()
    {
        var mock = new Mock<IRobotService>();
        mock.Setup(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Robot?)null);
        var client = CreateClient(mock.Object);

        var response = await client.GetAsync($"/api/robots/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Heartbeat_Existing_Returns200()
    {
        var id = Guid.NewGuid();
        var mock = new Mock<IRobotService>();
        mock.Setup(s => s.RecordHeartbeatAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Robot { Id = id, MachineName = "RPA-01", Status = RobotStatus.Online });
        var client = CreateClient(mock.Object);

        var response = await client.PutAsync($"/api/robots/{id}/heartbeat", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Heartbeat_Unknown_Returns404()
    {
        var mock = new Mock<IRobotService>();
        mock.Setup(s => s.RecordHeartbeatAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Robot?)null);
        var client = CreateClient(mock.Object);

        var response = await client.PutAsync($"/api/robots/{Guid.NewGuid()}/heartbeat", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private class RobotDto
    {
        public Guid Id { get; set; }
        public string MachineName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }
}

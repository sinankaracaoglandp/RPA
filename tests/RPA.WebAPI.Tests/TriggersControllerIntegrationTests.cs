namespace RPA.WebAPI.Tests;

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RPA.Domain.Entities;
using RPA.Domain.Interfaces;
using RPA.WebAPI.Triggers;

/// <summary>
/// TriggersController HTTP-seviyesi testleri (Task 3.3). ITriggerRepository/ITriggerService stub'lanır.
/// </summary>
public class TriggersControllerIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public TriggersControllerIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient(ITriggerRepository repository, ITriggerService? triggerService = null)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                foreach (var t in new[] { typeof(ITriggerRepository), typeof(ITriggerService) })
                {
                    var descriptor = services.SingleOrDefault(d => d.ServiceType == t);
                    if (descriptor is not null) services.Remove(descriptor);
                }
                services.AddScoped(_ => repository);
                services.AddScoped(_ => triggerService ?? new Mock<ITriggerService>().Object);
            });
        }).CreateClient();
    }

    [Fact]
    public async Task Create_ManualTrigger_Returns201()
    {
        var repo = new Mock<ITriggerRepository>();
        repo.Setup(r => r.AddTriggerAsync(It.IsAny<Trigger>(), It.IsAny<CancellationToken>()))
            .Callback<Trigger, CancellationToken>((t, _) => { }).Returns(Task.CompletedTask);
        var client = CreateClient(repo.Object);

        var response = await client.PostAsJsonAsync("/api/triggers", new CreateTriggerRequest
        {
            ProjectId = Guid.NewGuid(),
            WorkflowVersionId = Guid.NewGuid(),
            Type = "Manual",
            EnvironmentId = Guid.NewGuid(),
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TriggerDto>();
        Assert.Equal("Manual", body!.Type);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_CronTriggerWithoutSchedule_Returns400()
    {
        var repo = new Mock<ITriggerRepository>();
        var client = CreateClient(repo.Object);

        var response = await client.PostAsJsonAsync("/api/triggers", new CreateTriggerRequest
        {
            ProjectId = Guid.NewGuid(),
            WorkflowVersionId = Guid.NewGuid(),
            Type = "Cron",
            EnvironmentId = Guid.NewGuid(),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_CronTriggerWithSchedule_Returns201WithSchedule()
    {
        var repo = new Mock<ITriggerRepository>();
        var client = CreateClient(repo.Object);

        var response = await client.PostAsJsonAsync("/api/triggers", new CreateTriggerRequest
        {
            ProjectId = Guid.NewGuid(),
            WorkflowVersionId = Guid.NewGuid(),
            Type = "Cron",
            EnvironmentId = Guid.NewGuid(),
            Schedule = new ScheduleRequest { CronExpression = "0 9 * * *", TimeZone = "UTC", OverlapPolicy = "skip" },
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TriggerDto>();
        Assert.NotNull(body!.Schedule);
        Assert.Equal("0 9 * * *", body.Schedule!.CronExpression);
        repo.Verify(r => r.AddScheduleAsync(It.IsAny<Schedule>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetByWorkflowVersion_ReturnsTriggers()
    {
        var workflowVersionId = Guid.NewGuid();
        var trigger = new Trigger { WorkflowVersionId = workflowVersionId, Type = RPA.Domain.Enums.TriggerType.Manual, IsActive = true };
        var repo = new Mock<ITriggerRepository>();
        repo.Setup(r => r.FindTriggersByWorkflowVersionAsync(workflowVersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Trigger> { trigger });
        var client = CreateClient(repo.Object);

        var response = await client.GetAsync($"/api/workflows/{workflowVersionId}/triggers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<TriggerDto>>();
        Assert.Single(body!);
        Assert.Equal(trigger.Id, body![0].Id);
    }

    [Fact]
    public async Task Update_UnknownTrigger_Returns404()
    {
        var repo = new Mock<ITriggerRepository>();
        repo.Setup(r => r.FindTriggerByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Trigger?)null);
        var client = CreateClient(repo.Object);

        var response = await client.PatchAsync($"/api/triggers/{Guid.NewGuid()}", JsonContent.Create(new UpdateTriggerRequest { IsActive = false }));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_ExistingTrigger_UpdatesIsActive()
    {
        var trigger = new Trigger { IsActive = true, Type = RPA.Domain.Enums.TriggerType.Manual };
        var repo = new Mock<ITriggerRepository>();
        repo.Setup(r => r.FindTriggerByIdAsync(trigger.Id, It.IsAny<CancellationToken>())).ReturnsAsync(trigger);
        repo.Setup(r => r.FindScheduleByTriggerIdAsync(trigger.Id, It.IsAny<CancellationToken>())).ReturnsAsync((Schedule?)null);
        var client = CreateClient(repo.Object);

        var response = await client.PatchAsync($"/api/triggers/{trigger.Id}", JsonContent.Create(new UpdateTriggerRequest { IsActive = false }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(trigger.IsActive);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Fire_UnknownTrigger_Returns404()
    {
        var repo = new Mock<ITriggerRepository>();
        var svc = new Mock<ITriggerService>();
        svc.Setup(s => s.ExecuteTriggerAsync(It.IsAny<Guid>(), "manual", It.IsAny<CancellationToken>()))
            .ReturnsAsync(TriggerExecutionResult.NotFound());
        var client = CreateClient(repo.Object, svc.Object);

        var response = await client.PostAsync($"/api/triggers/{Guid.NewGuid()}/fire", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Fire_KnownTrigger_ReturnsExecutedOutcome()
    {
        var triggerId = Guid.NewGuid();
        var jobRun = new JobRun { Status = "Successful" };
        var repo = new Mock<ITriggerRepository>();
        var svc = new Mock<ITriggerService>();
        svc.Setup(s => s.ExecuteTriggerAsync(triggerId, "manual", It.IsAny<CancellationToken>()))
            .ReturnsAsync(TriggerExecutionResult.Executed(jobRun));
        var client = CreateClient(repo.Object, svc.Object);

        var response = await client.PostAsync($"/api/triggers/{triggerId}/fire", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TriggerFireResultDto>();
        Assert.Equal("Executed", body!.Outcome);
        Assert.Equal(jobRun.Id, body.JobRunId);
    }

    [Fact]
    public async Task Create_Then_List_ReturnsRobotTargeting()
    {
        var store = new List<Trigger>();
        var repo = new Mock<ITriggerRepository>();
        repo.Setup(r => r.AddTriggerAsync(It.IsAny<Trigger>(), It.IsAny<CancellationToken>()))
            .Callback<Trigger, CancellationToken>((t, _) => store.Add(t)).Returns(Task.CompletedTask);
        repo.Setup(r => r.ListTriggersAsync(It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<bool?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => store);
        var client = CreateClient(repo.Object);

        var create = new
        {
            projectId = Guid.NewGuid(),
            workflowVersionId = Guid.NewGuid(),
            type = "Manual",
            environmentId = Guid.NewGuid(),
            isActive = true,
            targetRobotTags = "prod-vm,sap",
            priority = 3,
        };
        var createResp = await client.PostAsJsonAsync("/api/triggers", create);
        createResp.EnsureSuccessStatusCode();

        var listResp = await client.GetAsync("/api/triggers");
        listResp.EnsureSuccessStatusCode();
        var list = await listResp.Content.ReadFromJsonAsync<List<TriggerDtoShape>>();

        Assert.Contains(list!, t => t.TargetRobotTags == "prod-vm,sap" && t.Priority == 3);
    }

    private sealed class TriggerDtoShape
    {
        public string TargetRobotTags { get; set; } = "";
        public int Priority { get; set; }
    }
}

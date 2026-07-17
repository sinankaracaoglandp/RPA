namespace RPA.WebAPI.Tests;

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Infrastructure.Authentication;
using RPA.Infrastructure.UISpy;

/// <summary>
/// UI Spy REST controller + StudioHub testleri (Task 4.4, Spec Bölüm 6).
/// </summary>
public class UiSpyTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public UiSpyTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private string GenerateToken()
    {
        using var scope = _factory.Services.CreateScope();
        var opts = scope.ServiceProvider.GetRequiredService<IOptions<AuthenticationOptions>>();
        return new JwtTokenService(opts).GenerateToken("studio-user", new[] { "Designer" });
    }

    private string GenerateAgentToken()
    {
        using var scope = _factory.Services.CreateScope();
        var opts = scope.ServiceProvider.GetRequiredService<IOptions<AuthenticationOptions>>();
        return new AgentTokenService(opts).GenerateAccessToken(new AgentIdentity
        {
            Id = Guid.NewGuid(),
            LicenseInstallationId = Guid.NewGuid(),
            Status = AgentIdentityStatus.Activated,
        });
    }

    private HttpClient AuthedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", GenerateToken());
        return client;
    }

    private HubConnection CreateHubConnection(string? token = null)
    {
        var server = _factory.Server;
        return new HubConnectionBuilder()
            .WithUrl($"{server.BaseAddress}hubs/studio", options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                if (token is not null)
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(token);
                }
            })
            .Build();
    }

    [Fact]
    public async Task Detect_WithoutAuth_IsUnauthorized()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/uispy/detect",
            new SpyElementMessage { ElementId = "wnd[0]/usr/btn[OK]" });

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Detect_WithEmptyElementId_ReturnsBadRequest()
    {
        var resp = await AuthedClient().PostAsJsonAsync("/api/uispy/detect",
            new SpyElementMessage { ElementId = "" });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Detect_WithValidElement_ReturnsOkAndEchoesElement()
    {
        var element = new SpyElementMessage { ElementId = "wnd[0]/usr/btn[OK]", Type = "GuiButton", Text = "OK", X = 5, Y = 6 };

        var resp = await AuthedClient().PostAsJsonAsync("/api/uispy/detect", element);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var echoed = await resp.Content.ReadFromJsonAsync<SpyElementMessage>();
        Assert.NotNull(echoed);
        Assert.Equal("wnd[0]/usr/btn[OK]", echoed!.ElementId);
    }

    [Fact]
    public async Task Detect_BroadcastsToConnectedStudioClient()
    {
        var token = GenerateToken();
        var connection = CreateHubConnection(token);

        var tcs = new TaskCompletionSource<SpyElementMessage>();
        connection.On<SpyElementMessage>(StudioHub_DetectedElementEvent, el => tcs.TrySetResult(el));
        await connection.StartAsync();

        var element = new SpyElementMessage { ElementId = "wnd[0]/usr/ctxtRMMG1-MATNR", Type = "GuiCTextField" };
        var resp = await AuthedClient().PostAsJsonAsync("/api/uispy/detect", element);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Equal(tcs.Task, completed);
        Assert.Equal("wnd[0]/usr/ctxtRMMG1-MATNR", (await tcs.Task).ElementId);
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task StudioHub_RoutesDetectedElementOnlyToSessionOwner()
    {
        var token = GenerateToken();
        var owner = CreateHubConnection(token);
        var other = CreateHubConnection(token);
        var agent = CreateHubConnection(GenerateAgentToken());
        var sessionId = Guid.NewGuid();
        var ownerReceived = new TaskCompletionSource<SpyElementMessage>();
        var otherReceived = new TaskCompletionSource<SpyElementMessage>();

        owner.On<SpyElementMessage>(StudioHub_DetectedElementEvent, el => ownerReceived.TrySetResult(el));
        other.On<SpyElementMessage>(StudioHub_DetectedElementEvent, el => otherReceived.TrySetResult(el));

        await owner.StartAsync();
        await other.StartAsync();
        await agent.StartAsync();
        await owner.InvokeAsync(StudioHub_StartSpyCommand, sessionId, "sap", (string?)null);

        await agent.InvokeAsync("ReceiveDetectedElement", new SpyElementMessage
        {
            SessionId = sessionId,
            Kind = "sap",
            ElementId = "wnd[0]/usr/ctxtRMMG1-MATNR",
        });

        var completed = await Task.WhenAny(ownerReceived.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Equal(ownerReceived.Task, completed);
        Assert.Equal(sessionId, (await ownerReceived.Task).SessionId);
        var otherCompleted = await Task.WhenAny(otherReceived.Task, Task.Delay(TimeSpan.FromMilliseconds(300)));
        Assert.NotEqual(otherReceived.Task, otherCompleted);

        await owner.DisposeAsync();
        await other.DisposeAsync();
        await agent.DisposeAsync();
    }

    [Fact]
    public async Task StudioHub_StopSpy_RemovesSessionRoute()
    {
        var token = GenerateToken();
        var owner = CreateHubConnection(token);
        var agent = CreateHubConnection(GenerateAgentToken());
        var sessionId = Guid.NewGuid();
        var received = new TaskCompletionSource<SpyElementMessage>();

        owner.On<SpyElementMessage>(StudioHub_DetectedElementEvent, el => received.TrySetResult(el));

        await owner.StartAsync();
        await agent.StartAsync();
        await owner.InvokeAsync(StudioHub_StartSpyCommand, sessionId, "sap", (string?)null);
        await owner.InvokeAsync(StudioHub_StopSpyCommand, sessionId);
        await agent.InvokeAsync("ReceiveDetectedElement", new SpyElementMessage
        {
            SessionId = sessionId,
            Kind = "sap",
            ElementId = "wnd[0]/usr/btn[OK]",
        });

        var completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromMilliseconds(300)));
        Assert.NotEqual(received.Task, completed);

        await owner.DisposeAsync();
        await agent.DisposeAsync();
    }

    [Fact]
    public async Task StudioHub_StopSpy_ByNonOwner_DoesNotRemoveSessionRoute()
    {
        var token = GenerateToken();
        var owner = CreateHubConnection(token);
        var other = CreateHubConnection(token);
        var agent = CreateHubConnection(GenerateAgentToken());
        var sessionId = Guid.NewGuid();
        var received = new TaskCompletionSource<SpyElementMessage>();

        owner.On<SpyElementMessage>(StudioHub_DetectedElementEvent, el => received.TrySetResult(el));

        await owner.StartAsync();
        await other.StartAsync();
        await agent.StartAsync();
        await owner.InvokeAsync(StudioHub_StartSpyCommand, sessionId, "sap", (string?)null);
        await other.InvokeAsync(StudioHub_StopSpyCommand, sessionId);
        await agent.InvokeAsync("ReceiveDetectedElement", new SpyElementMessage
        {
            SessionId = sessionId,
            Kind = "sap",
            ElementId = "wnd[0]/usr/btn[OK]",
        });

        var completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Equal(received.Task, completed);
        Assert.Equal(sessionId, (await received.Task).SessionId);

        await owner.DisposeAsync();
        await other.DisposeAsync();
        await agent.DisposeAsync();
    }

    [Fact]
    public async Task StudioHub_StartSpy_RejectsUnsupportedKind()
    {
        var connection = CreateHubConnection(GenerateToken());
        await connection.StartAsync();

        await Assert.ThrowsAnyAsync<Exception>(
            () => connection.InvokeAsync(StudioHub_StartSpyCommand, Guid.NewGuid(), "mainframe", (string?)null));

        await connection.DisposeAsync();
    }

    [Theory]
    [InlineData("sap")]
    [InlineData("web")]
    [InlineData("desktop")]
    [InlineData("image")]
    [InlineData("text-offset")]
    public async Task StudioHub_StartSpy_AcceptsSupportedKind(string kind)
    {
        var connection = CreateHubConnection(GenerateToken());
        await connection.StartAsync();

        // Desteklenen tip HubException fırlatmamalı (image = Paket F görüntü/OCR picker'ı).
        await connection.InvokeAsync(StudioHub_StartSpyCommand, Guid.NewGuid(), kind, (string?)null);

        await connection.DisposeAsync();
    }

    [Fact]
    public async Task StudioHub_WithoutToken_IsRejected()
    {
        var connection = CreateHubConnection();

        await Assert.ThrowsAnyAsync<Exception>(() => connection.StartAsync());
        await connection.DisposeAsync();
    }

    // StudioHub.DetectedElementEvent değeri (test bağımlılığını azaltmak için sabit).
    private const string StudioHub_DetectedElementEvent = "DetectedElement";
    private const string StudioHub_StartSpyCommand = "StartSpy";
    private const string StudioHub_StopSpyCommand = "StopSpy";
}

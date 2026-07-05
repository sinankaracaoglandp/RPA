namespace RPA.WebAPI.Tests;

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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

    private HttpClient AuthedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", GenerateToken());
        return client;
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
        var server = _factory.Server;
        var connection = new HubConnectionBuilder()
            .WithUrl($"{server.BaseAddress}hubs/studio", options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();

        var tcs = new TaskCompletionSource<SpyElementMessage>();
        connection.On<SpyElementMessage>(StudioHub_DetectedElementEvent, el => tcs.TrySetResult(el));
        await connection.StartAsync();

        var element = new SpyElementMessage { ElementId = "wnd[0]/usr/ctxtRMMG1-MATNR", Type = "GuiCTextField" };
        var resp = await AuthedClient().PostAsJsonAsync("/api/uispy/detect", element);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Equal(tcs.Task, completed);
        Assert.Equal("wnd[0]/usr/ctxtRMMG1-MATNR", tcs.Task.Result.ElementId);
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task StudioHub_WithoutToken_IsRejected()
    {
        var server = _factory.Server;
        var connection = new HubConnectionBuilder()
            .WithUrl($"{server.BaseAddress}hubs/studio", options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();

        await Assert.ThrowsAnyAsync<Exception>(() => connection.StartAsync());
        await connection.DisposeAsync();
    }

    // StudioHub.DetectedElementEvent değeri (test bağımlılığını azaltmak için sabit).
    private const string StudioHub_DetectedElementEvent = "DetectedElement";
}

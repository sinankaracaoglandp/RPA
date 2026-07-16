namespace RPA.Agent.Tests.Authentication;

using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.Extensions.Options;
using RPA.Agent.Authentication;
using RPA.Agent.Configuration;

/// <summary>
/// Task 5 (izleme): tum ajan hub baglantilari tek bir fabrikadan uretilir. Fabrika, paylasilan
/// token saglayicisini SignalR'in <see cref="HttpConnectionOptions.AccessTokenProvider"/>'ina baglar.
/// Testler yalnizca genel API yuzeyini kullanir — SignalR'in ic alanlarina yansima ile bakilmaz.
/// </summary>
public sealed class AgentHubConnectionFactoryTests
{
    private const string Token = "fake.access.token";

    private sealed class StubTokenProvider : IAgentAccessTokenProvider
    {
        public int Calls;
        public Task<string> GetTokenAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(Token);
        }
    }

    private static AgentHubConnectionFactory Create(
        StubTokenProvider provider,
        string orchestratorUrl = "https://orchestrator:5001")
        => new(
            Options.Create(new AgentOptions
            {
                OrchestratorUrl = orchestratorUrl,
                AgentId = Guid.NewGuid(),
            }),
            provider);

    [Fact]
    public async Task ConfigureHttpConnection_BindsSharedTokenProvider()
    {
        var provider = new StubTokenProvider();
        var options = new HttpConnectionOptions();

        Create(provider).ConfigureHttpConnection(options);

        Assert.NotNull(options.AccessTokenProvider);
        Assert.Equal(0, provider.Calls); // baglama aninda token istenmez — yalnizca baglanirken.
        Assert.Equal(Token, await options.AccessTokenProvider!());
        Assert.Equal(1, provider.Calls);
    }

    [Theory]
    [InlineData("https://orchestrator:5001", "/hubs/robot")]
    [InlineData("https://orchestrator:5001/", "/hubs/robot")]
    [InlineData("https://orchestrator:5001", "hubs/robot")]
    public void BuildHubUrl_JoinsOrchestratorUrlAndPath(string orchestratorUrl, string hubPath)
    {
        var url = Create(new StubTokenProvider(), orchestratorUrl).BuildHubUrl(hubPath);

        Assert.Equal("https://orchestrator:5001/hubs/robot", url.ToString());
    }

    [Fact]
    public async Task Create_BuildsConnectionForRequestedHub()
    {
        await using var connection = Create(new StubTokenProvider()).Create("/hubs/studio");

        Assert.NotNull(connection);
    }

    [Fact]
    public void Constructor_RejectsMissingDependencies()
    {
        var options = Options.Create(new AgentOptions { OrchestratorUrl = "https://orchestrator:5001" });

        Assert.Throws<ArgumentNullException>(() => new AgentHubConnectionFactory(null!, new StubTokenProvider()));
        Assert.Throws<ArgumentNullException>(() => new AgentHubConnectionFactory(options, null!));
    }
}

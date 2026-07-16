namespace RPA.Agent.Tests.UISpy;

using System.Reflection;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RPA.Agent.Authentication;
using RPA.Agent.Configuration;
using RPA.Agent.Hub;
using RPA.Agent.Prompts;
using RPA.Agent.UISpy;

/// <summary>
/// Task 5: RobotHub ve her iki StudioHub baglantisi da paylasilan token saglayicisini
/// SignalR <c>AccessTokenProvider</c> uzerinden kullanmalidir.
/// </summary>
public sealed class SpyHubAuthenticationTests
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

    private static IOptions<AgentOptions> Options()
        => Microsoft.Extensions.Options.Options.Create(new AgentOptions
        {
            OrchestratorUrl = "https://orchestrator:5001",
            AgentId = Guid.NewGuid(),
        });

    /// <summary>Kurulmus bir HubConnection'in HttpConnectionOptions'ini yansima ile cikarir.</summary>
    private static HttpConnectionOptions ExtractHttpOptions(HubConnection connection)
    {
        var factory = GetField(connection, "_connectionFactory")
            ?? throw new InvalidOperationException("HubConnection._connectionFactory bulunamadi.");
        var options = GetField(factory, "_httpConnectionOptions")
            ?? throw new InvalidOperationException("HttpConnectionFactory._httpConnectionOptions bulunamadi.");
        return (HttpConnectionOptions)options;

        static object? GetField(object target, string name)
        {
            for (var t = target.GetType(); t is not null; t = t.BaseType)
            {
                var f = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
                if (f is not null) return f.GetValue(target);
            }
            return null;
        }
    }

    private static HubConnection ExtractConnection(object client)
    {
        var f = client.GetType().GetField("_connection", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("_connection alani bulunamadi.");
        return (HubConnection)f.GetValue(client)!;
    }

    private static async Task AssertUsesTokenProviderAsync(object client, StubTokenProvider provider, string expectedPath)
    {
        var options = ExtractHttpOptions(ExtractConnection(client));
        Assert.NotNull(options.AccessTokenProvider);
        Assert.EndsWith(expectedPath, options.Url!.AbsolutePath, StringComparison.Ordinal);

        var token = await options.AccessTokenProvider!();
        Assert.Equal(Token, token);
        Assert.True(provider.Calls > 0);
    }

    [Fact]
    public async Task RobotHubClient_ConfiguresAccessTokenProvider()
    {
        var provider = new StubTokenProvider();
        var client = new RobotHubClient(
            Options(),
            new HubConnectionStatusCoordinator(NullLogger<HubConnectionStatusCoordinator>.Instance),
            new JobEventRouter(new RPA.Agent.JobList.JobListViewModel(), NullLogger<JobEventRouter>.Instance),
            new UserPromptService(NullLogger<UserPromptService>.Instance),
            provider,
            NullLogger<RobotHubClient>.Instance);

        await AssertUsesTokenProviderAsync(client, provider, "/hubs/robot");
    }

    [Fact]
    public async Task SpyCommandConnection_ConfiguresAccessTokenProvider()
    {
        var provider = new StubTokenProvider();
        var client = new SignalRSpyCommandConnection(Options(), provider);

        await AssertUsesTokenProviderAsync(client, provider, "/hubs/studio");
    }

    [Fact]
    public async Task SpyElementTransport_ConfiguresAccessTokenProvider()
    {
        var provider = new StubTokenProvider();
        var client = new SignalRSpyElementTransport(Options(), provider);

        await AssertUsesTokenProviderAsync(client, provider, "/hubs/studio");
    }
}

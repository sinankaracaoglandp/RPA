namespace RPA.Agent.Tests.UISpy;

using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging.Abstractions;
using RPA.Agent.Authentication;
using RPA.Agent.Hub;
using RPA.Agent.Prompts;
using RPA.Agent.UISpy;

/// <summary>
/// Task 5: RobotHub ve her iki StudioHub istemcisi de baglantilarini paylasilan
/// <see cref="IAgentHubConnectionFactory"/> uzerinden kurar — ajan JWT'si tek bir yerde baglanir.
/// Fabrikanin token'i gercekten bagladigi <c>AgentHubConnectionFactoryTests</c>'te dogrulanir;
/// burada her istemcinin dogru hub yolunu fabrikadan istedigi dogrulanir.
/// </summary>
public sealed class SpyHubAuthenticationTests
{
    /// <summary>Istenen hub yollarini kaydeder; uretilen baglanti hicbir zaman baslatilmaz.</summary>
    private sealed class RecordingHubConnectionFactory : IAgentHubConnectionFactory
    {
        public List<string> RequestedPaths { get; } = [];

        public HubConnection Create(string hubPath)
        {
            RequestedPaths.Add(hubPath);
            return new HubConnectionBuilder()
                .WithUrl($"https://orchestrator.test{hubPath}")
                .Build();
        }
    }

    [Fact]
    public void RobotHubClient_BuildsConnectionThroughSharedFactory()
    {
        var factory = new RecordingHubConnectionFactory();

        _ = new RobotHubClient(
            factory,
            new HubConnectionStatusCoordinator(NullLogger<HubConnectionStatusCoordinator>.Instance),
            new JobEventRouter(new RPA.Agent.JobList.JobListViewModel(), NullLogger<JobEventRouter>.Instance),
            new UserPromptService(NullLogger<UserPromptService>.Instance),
            NullLogger<RobotHubClient>.Instance);

        Assert.Equal(["/hubs/robot"], factory.RequestedPaths);
    }

    [Fact]
    public void SpyCommandConnection_BuildsConnectionThroughSharedFactory()
    {
        var factory = new RecordingHubConnectionFactory();

        _ = new SignalRSpyCommandConnection(factory);

        Assert.Equal(["/hubs/studio"], factory.RequestedPaths);
    }

    [Fact]
    public void SpyElementTransport_BuildsConnectionThroughSharedFactory()
    {
        var factory = new RecordingHubConnectionFactory();

        _ = new SignalRSpyElementTransport(factory);

        Assert.Equal(["/hubs/studio"], factory.RequestedPaths);
    }

    [Fact]
    public void Clients_RejectMissingFactory()
    {
        Assert.Throws<ArgumentNullException>(() => new SignalRSpyCommandConnection(null!));
        Assert.Throws<ArgumentNullException>(() => new SignalRSpyElementTransport(null!));
    }
}

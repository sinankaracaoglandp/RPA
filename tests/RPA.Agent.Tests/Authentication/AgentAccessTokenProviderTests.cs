namespace RPA.Agent.Tests.Authentication;

using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;
using RPA.Agent.Authentication;
using RPA.Agent.Configuration;

/// <summary>
/// Ajan erisim tokeni saglayicisi (Task 5): tek istek serilestirme, onbellek, 2 dk yenileme
/// penceresi ve hata durumunda credential sizdirmama davranisi.
/// </summary>
public sealed class AgentAccessTokenProviderTests
{
    private const string Credential = "super-secret-agent-credential";

    private static string CreateJwt(TimeSpan lifetime)
    {
        using var rsa = RSA.Create(2048);
        var creds = new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256);
        var token = new JwtSecurityToken(
            issuer: "rpa",
            audience: "rpa",
            claims: null,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.Add(lifetime),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class FakeTokenClient : IAgentTokenClient
    {
        private readonly Func<TimeSpan> _lifetime;
        public int CallCount;
        public TaskCompletionSource? Gate;
        public Exception? Throw;
        public string? LastCredential;

        public FakeTokenClient(Func<TimeSpan>? lifetime = null)
            => _lifetime = lifetime ?? (() => TimeSpan.FromMinutes(10));

        public async Task<string> RequestAccessTokenAsync(Guid agentId, string credential, CancellationToken ct)
        {
            Interlocked.Increment(ref CallCount);
            LastCredential = credential;
            if (Gate is not null) await Gate.Task;
            if (Throw is not null) throw Throw;
            return CreateJwt(_lifetime());
        }
    }

    private sealed class FakeCredentialStore : IAgentCredentialStore
    {
        private string? _credential;
        public FakeCredentialStore(string? credential) => _credential = credential;
        public string? TryGetCredential() => _credential;
        public void SaveCredential(string credential) => _credential = credential;
        public void Clear() => _credential = null;
    }

    private static AgentAccessTokenProvider CreateProvider(
        IAgentTokenClient client, IAgentCredentialStore? store = null)
    {
        var options = Options.Create(new AgentOptions
        {
            OrchestratorUrl = "https://orchestrator:5001",
            AgentId = Guid.NewGuid(),
        });
        return new AgentAccessTokenProvider(
            client,
            store ?? new FakeCredentialStore(Credential),
            options,
            NullLogger<AgentAccessTokenProvider>.Instance);
    }

    [Fact]
    public async Task ConcurrentCalls_PerformSingleTokenRequest()
    {
        var client = new FakeTokenClient { Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        var provider = CreateProvider(client);

        var calls = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => provider.GetTokenAsync(CancellationToken.None)))
            .ToArray();

        await Task.Delay(50);
        client.Gate.SetResult();
        var tokens = await Task.WhenAll(calls);

        Assert.Equal(1, client.CallCount);
        Assert.All(tokens, t => Assert.Equal(tokens[0], t));
    }

    [Fact]
    public async Task CachedToken_IsReusedOutsideRenewalWindow()
    {
        var client = new FakeTokenClient();
        var provider = CreateProvider(client);

        var first = await provider.GetTokenAsync(CancellationToken.None);
        var second = await provider.GetTokenAsync(CancellationToken.None);

        Assert.Equal(first, second);
        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public async Task ExpiringToken_IsRefreshedInsideTwoMinuteWindow()
    {
        // 90 sn omurlu token proaktif yenileme penceresi (2 dk) icindedir → her cagri yeniler.
        var client = new FakeTokenClient(() => TimeSpan.FromSeconds(90));
        var provider = CreateProvider(client);

        await provider.GetTokenAsync(CancellationToken.None);
        await provider.GetTokenAsync(CancellationToken.None);

        Assert.Equal(2, client.CallCount);
    }

    [Fact]
    public async Task FailedRefresh_DoesNotExposeCredential()
    {
        var client = new FakeTokenClient { Throw = new HttpRequestException("AGENT_CREDENTIAL_INVALID") };
        var provider = CreateProvider(client);

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => provider.GetTokenAsync(CancellationToken.None));
        Assert.DoesNotContain(Credential, ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingCredential_FailsWithoutTokenRequest()
    {
        var client = new FakeTokenClient();
        var provider = CreateProvider(client, new FakeCredentialStore(null));

        await Assert.ThrowsAnyAsync<Exception>(() => provider.GetTokenAsync(CancellationToken.None));
        Assert.Equal(0, client.CallCount);
    }
}

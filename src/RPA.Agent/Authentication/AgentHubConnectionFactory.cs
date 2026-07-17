namespace RPA.Agent.Authentication;

using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;
using RPA.Agent.Configuration;

/// <summary>
/// Ajan sürecindeki tüm SignalR hub bağlantılarının tek üretim noktası (Task 5).
/// Orkestratör adresi ve ajan JWT'si burada bağlanır; istemciler yalnızca hub yolunu ister.
/// </summary>
public interface IAgentHubConnectionFactory
{
    /// <summary>Verilen hub yolu için (örn. <c>/hubs/robot</c>) yapılandırılmış bağlantı üretir.</summary>
    HubConnection Create(string hubPath);
}

/// <summary>
/// <see cref="IAgentHubConnectionFactory"/>'nin gerçek implementasyonu: orkestratör URL'i +
/// paylaşılan token sağlayıcısı + otomatik yeniden bağlanma.
/// </summary>
/// <remarks>
/// Token bağlama mantığı bilerek tek bir yerde tutulur: önceden üç istemci aynı
/// <c>WithUrl(..., o =&gt; o.AccessTokenProvider = ...)</c> lambda'sını kopyalıyordu ve testler
/// bunu SignalR'in private alanlarına yansıma ile bakarak doğrulamak zorunda kalıyordu.
/// <see cref="ConfigureHttpConnection"/> ve <see cref="BuildHubUrl"/> public tutulur ki testler
/// yalnızca genel API yüzeyi üzerinden (yansıma olmadan) doğrulayabilsin.
/// </remarks>
public sealed class AgentHubConnectionFactory : IAgentHubConnectionFactory
{
    private readonly string _orchestratorUrl;
    private readonly IAgentAccessTokenProvider _tokenProvider;

    public AgentHubConnectionFactory(
        IOptions<AgentOptions> options,
        IAgentAccessTokenProvider tokenProvider)
    {
        var value = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        _orchestratorUrl = value.OrchestratorUrl?.TrimEnd('/')
            ?? throw new ArgumentNullException(nameof(options), "OrchestratorUrl gereklidir.");
    }

    /// <summary>Orkestratör adresi ile hub yolunu birleştirir (her iki tarafta da eğik çizgi toleranslı).</summary>
    public Uri BuildHubUrl(string hubPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hubPath);
        return new Uri($"{_orchestratorUrl}/{hubPath.TrimStart('/')}");
    }

    /// <summary>
    /// Ajan JWT'sini SignalR'in <see cref="HttpConnectionOptions.AccessTokenProvider"/>'ına bağlar.
    /// SignalR her (yeniden) bağlantıda çağırır; böylece kısa ömürlü token şeffaf şekilde yenilenir.
    /// Bağlama anında token istenmez — yalnızca bağlanırken.
    /// </summary>
    public void ConfigureHttpConnection(HttpConnectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.AccessTokenProvider = () => _tokenProvider.GetTokenAsync(CancellationToken.None);
    }

    public HubConnection Create(string hubPath)
        => new HubConnectionBuilder()
            .WithUrl(BuildHubUrl(hubPath), ConfigureHttpConnection)
            .WithAutomaticReconnect()
            .Build();
}

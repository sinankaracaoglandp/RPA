namespace RPA.Infrastructure.SAP.SapGuiElements;

using RPA.Domain.Interfaces;
using BusinessException = RPA.Domain.Exceptions.BusinessException;

/// <summary>
/// SAP GUI oturumuna bağlanır (Spec 5.3 — SAP GUI: Connect/Login).
/// Şifre Credential Vault'tan çözülür (plaintext taşınmaz).
/// </summary>
public sealed class SapGuiConnectActivity : IActivity
{
    private readonly ISapGuiChannel _channel;

    public SapGuiConnectActivity(ISapGuiChannel channel)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
    }

    public ActivityMetadata GetMetadata() => new()
    {
        ActivityId = "Sap.Gui.Connect",
        DisplayName = "SAP GUI Bağlan",
        Category = "SAP",
        Description = "SAP GUI Scripting oturumu açar (fallback kanal).",
        Inputs = new()
        {
            new ActivityParameter { Name = "systemId", Type = "string", Required = true, Description = "SAP System ID (örn. DEV)" },
            new ActivityParameter { Name = "client", Type = "string", Required = true, Description = "SAP Client (örn. 100)" },
            new ActivityParameter { Name = "userId", Type = "string", Required = true, Description = "SAP kullanıcı adı" },
            new ActivityParameter { Name = "credentialName", Type = "Credential", Required = true, Description = "Şifre için Vault key referansı" },
            new ActivityParameter { Name = "language", Type = "string", Required = false, DefaultValue = "EN", Description = "Oturum dili" }
        },
        Outputs = new(),
        RequiredCapabilities = new() { "sap-gui" }
    };

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var systemId = context.GetVariable<string>("systemId");
        var client = context.GetVariable<string>("client");
        var userId = context.GetVariable<string>("userId");
        var credentialName = context.GetVariable<string>("credentialName");
        var language = context.GetVariable<string?>("language");

        if (string.IsNullOrWhiteSpace(systemId))
        {
            throw new BusinessException("'systemId' parametresi boş olamaz.");
        }

        if (string.IsNullOrWhiteSpace(credentialName))
        {
            throw new BusinessException("'credentialName' parametresi boş olamaz.");
        }

        var password = await context.GetCredentialAsync(credentialName);

        context.Log($"SAP GUI bağlantısı açılıyor: {systemId}/{client} (kullanıcı: {userId}). Parola: [MASKED]");
        await _channel.ConnectAsync(systemId, client, userId, password, string.IsNullOrWhiteSpace(language) ? "EN" : language);
        context.Log("SAP GUI bağlantısı açıldı.");

        return new();
    }
}

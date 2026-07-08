namespace RPA.Infrastructure.Workflow.Activities.Web;

using RPA.Domain.Interfaces;

/// <summary>
/// Web workflow'lari icin tarayici oturumu baslatma aktivitesi.
/// Playwright tabanli gercek otomasyon WP-5.6 kapsaminda genisletilecek.
/// </summary>
public sealed class WebOpenActivity : IActivity
{
    private readonly IWebAutomationSessionManager _sessionManager;

    public WebOpenActivity(IWebAutomationSessionManager sessionManager)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
    }

    public async Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
    {
        var browser = context.GetVariable<string>("browser");
        var headless = context.GetVariable<bool?>("headless") ?? false;

        if (string.IsNullOrWhiteSpace(browser))
        {
            browser = "chromium";
        }

        var session = await _sessionManager.OpenAsync(browser, headless);

        context.SetVariable("session", session);
        context.Log($"Web oturumu hazirlandi: {browser}");

        return new Dictionary<string, object?>
        {
            ["session"] = session,
        };
    }

    public ActivityMetadata GetMetadata() => new()
    {
        ActivityId = "Web.Open",
        DisplayName = "Tarayici Ac",
        Category = ActivityRegistry.CatWeb,
        Description = "Yeni tarayici oturumu acar.",
        Inputs = new()
        {
            new ActivityParameter { Name = "browser", Type = "string", Required = false, DefaultValue = "chromium" },
            new ActivityParameter { Name = "headless", Type = "bool", Required = false, DefaultValue = false },
        },
        Outputs = new()
        {
            new ActivityParameter { Name = "session", Type = "JSON", Required = true },
        },
        RequiredCapabilities = new() { "web" },
    };
}

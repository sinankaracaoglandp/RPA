namespace RPA.Infrastructure.Workflow;

using Microsoft.Extensions.DependencyInjection;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Idempotency;
using RPA.Infrastructure.Persistence;
using RPA.Infrastructure.Workflow.Activities.Api;
using RPA.Infrastructure.Workflow.Activities.Email;
using RPA.Infrastructure.Workflow.Activities.Web;

public static class WorkflowServiceCollectionExtensions
{
    public static IServiceCollection AddWorkflowServices(this IServiceCollection services)
    {
        services.AddSingleton<ActivityCatalog>();
        services.AddTransient<WorkflowValidator>();
        services.AddSingleton<ICheckpointManager, CheckpointManager>();
        services.AddScoped<IQueueItemRepository, EfQueueItemRepository>();
        services.AddScoped<IIdempotencyService, IdempotencyService>();

        // Task 2.6.1: API HTTP aktivitesi
        services.AddHttpClient("Api.HttpRequest");
        services.AddKeyedTransient<IActivity, ApiHttpActivity>("Api.HttpRequest");

        // Task 2.8.1: E-posta aktiviteleri (Spec Bölüm 5.3)
        services.AddKeyedTransient<IActivity, EmailSendActivity>("Email.Send");
        services.AddKeyedTransient<IActivity, EmailReadInboxActivity>("Email.ReadInbox");
        services.AddKeyedTransient<IActivity, AttachmentDownloadActivity>("Email.DownloadAttachment");

        // WP-5.6 Web aktiviteleri: Studio katalogundan calistirilan Web.* node'lari.
        services.AddSingleton<IWebAutomationSessionManager, PlaywrightWebAutomationSessionManager>();
        services.AddKeyedTransient<IActivity, WebOpenActivity>("Web.Open");
        services.AddKeyedTransient<IActivity, WebGotoActivity>("Web.Goto");
        services.AddKeyedTransient<IActivity, WebClickActivity>("Web.Click");
        services.AddKeyedTransient<IActivity, WebFillActivity>("Web.Fill");
        services.AddKeyedTransient<IActivity, WebGetTextActivity>("Web.GetText");
        services.AddKeyedTransient<IActivity, WebWaitForActivity>("Web.WaitFor");
        services.AddKeyedTransient<IActivity, WebDownloadActivity>("Web.Download");
        services.AddKeyedTransient<IActivity, WebUploadActivity>("Web.Upload");
        services.AddKeyedTransient<IActivity, WebScreenshotActivity>("Web.Screenshot");
        services.AddKeyedTransient<IActivity, WebFrameSwitchActivity>("Web.FrameSwitch");

        services.AddSingleton<IActivityFactory>(sp =>
            new DelegateActivityFactory(activityId => sp.GetKeyedService<IActivity>(activityId)));

        services.AddTransient<IWorkflowRunner, BaseRunner>();

        // Task 4.5: Component publish/approve governance (Spec Bölüm 9).
        // Bellek-içi depo — DB persistansı ayrı bir görev kapsamındadır.
        services.AddSingleton<InMemoryComponentPublishRepository>();
        services.AddSingleton<IComponentPublishRepository>(sp => sp.GetRequiredService<InMemoryComponentPublishRepository>());
        services.AddSingleton<IComponentStore>(sp => sp.GetRequiredService<InMemoryComponentPublishRepository>());
        services.AddSingleton<RPA.Infrastructure.Services.ComponentPublishService>();
        return services;
    }
}

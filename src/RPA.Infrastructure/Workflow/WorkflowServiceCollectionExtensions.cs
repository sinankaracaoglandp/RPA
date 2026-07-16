namespace RPA.Infrastructure.Workflow;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RPA.Application.EInvoiceProfiles;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Activities.Desktop;
using RPA.Infrastructure.Idempotency;
using RPA.Infrastructure.Persistence;
using RPA.Infrastructure.Services;
using RPA.Infrastructure.Workflow.Activities.Api;
using RPA.Infrastructure.Workflow.Activities.Email;
using RPA.Infrastructure.Workflow.Activities.EInvoice;
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
        services.AddSingleton<UblInvoiceParser>();
        services.AddScoped<EInvoiceProfileDefinitionValidator>();
        services.AddScoped<EInvoiceProfileService>();
        services.AddSingleton<EInvoiceProfileExtractor>();
        services.AddKeyedTransient<IActivity, ReadUblActivity>("EInvoice.ReadUbl");
        services.AddKeyedTransient<IActivity, ReadUblBatchActivity>("EInvoice.ReadUblBatch");
        services.AddKeyedTransient<IActivity, ReadProfileActivity>("EInvoice.ReadProfile");
        services.AddKeyedTransient<IActivity, ReadProfileBatchActivity>("EInvoice.ReadProfileBatch");

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

        // Paket E: Windows Masaüstü aktiviteleri (Desktop.*). IDesktopAutomationChannel
        // implementasyonu (FlaUI/UIA) RPA.Agent sürecinde kayıtlıdır; aktiviteler yalnız
        // arayüze bağlıdır, bu yüzden çalıştırma anında (robot süreci) çözülür.
        services.TryAddSingleton<IDesktopAutomationChannel, UnavailableDesktopAutomationChannel>();
        services.AddKeyedTransient<IActivity, DesktopAttachActivity>("Desktop.Attach");
        services.AddKeyedTransient<IActivity, DesktopLaunchActivity>("Desktop.Launch");
        services.AddKeyedTransient<IActivity, DesktopClickActivity>("Desktop.Click");
        services.AddKeyedTransient<IActivity, DesktopSetTextActivity>("Desktop.SetText");
        services.AddKeyedTransient<IActivity, DesktopGetTextActivity>("Desktop.GetText");
        services.AddKeyedTransient<IActivity, DesktopSelectItemActivity>("Desktop.SelectItem");
        services.AddKeyedTransient<IActivity, DesktopSendKeysActivity>("Desktop.SendKeys");
        services.AddKeyedTransient<IActivity, DesktopWaitForActivity>("Desktop.WaitFor");
        services.AddKeyedTransient<IActivity, DesktopScreenshotActivity>("Desktop.Screenshot");

        // Kod & Veri: C# kod aktivitesi (Roslyn) + DataTable dönüşümleri.
        services.AddKeyedTransient<IActivity, Activities.Code.InvokeCsharpActivity>("System.InvokeCode");
        services.AddKeyedTransient<IActivity, Activities.Code.DataToDataTableActivity>("Data.ToDataTable");
        services.AddKeyedTransient<IActivity, Activities.Code.DataFromDataTableActivity>("Data.FromDataTable");

        // Paket F: Görüntü/OCR fallback aktiviteleri (Vision.*). IVisionAutomationChannel
        // implementasyonu Agent sürecinde kayıtlıdır; aktiviteler yalnız arayüze bağlıdır.
        services.TryAddSingleton<IVisionAutomationChannel, RPA.Infrastructure.Activities.Vision.UnavailableVisionAutomationChannel>();
        services.AddKeyedTransient<IActivity, RPA.Infrastructure.Activities.Vision.VisionClickActivity>("Vision.Click");
        services.AddKeyedTransient<IActivity, RPA.Infrastructure.Activities.Vision.VisionClickSequenceActivity>("Vision.ClickSequence");
        services.AddKeyedTransient<IActivity, RPA.Infrastructure.Activities.Vision.VisionWaitForActivity>("Vision.WaitFor");
        services.AddKeyedTransient<IActivity, RPA.Infrastructure.Activities.Vision.VisionExistsActivity>("Vision.Exists");
        services.AddKeyedTransient<IActivity, RPA.Infrastructure.Activities.Vision.VisionGetTextActivity>("Vision.GetText");
        services.AddKeyedTransient<IActivity, RPA.Infrastructure.Activities.Vision.VisionClickTextActivity>("Vision.ClickText");
        services.AddKeyedTransient<IActivity, RPA.Infrastructure.Activities.Vision.VisionClickTextOffsetActivity>("Vision.ClickTextOffset");
        services.AddKeyedTransient<IActivity, RPA.Infrastructure.Activities.Vision.VisionTextExistsActivity>("Vision.TextExists");

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

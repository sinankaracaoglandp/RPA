namespace RPA.Infrastructure.Workflow;

using Microsoft.Extensions.DependencyInjection;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Idempotency;
using RPA.Infrastructure.Persistence;
using RPA.Infrastructure.Workflow.Activities.Api;
using RPA.Infrastructure.Workflow.Activities.Email;

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

        services.AddSingleton<IActivityFactory>(sp =>
            new DelegateActivityFactory(activityId => sp.GetKeyedService<IActivity>(activityId)));

        services.AddTransient<IWorkflowRunner, BaseRunner>();
        return services;
    }
}

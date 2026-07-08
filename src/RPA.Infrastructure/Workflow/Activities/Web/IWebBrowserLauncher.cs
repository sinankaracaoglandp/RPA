namespace RPA.Infrastructure.Workflow.Activities.Web;

public interface IWebBrowserLauncher
{
    Task OpenAsync(Uri uri, CancellationToken cancellationToken = default);
}

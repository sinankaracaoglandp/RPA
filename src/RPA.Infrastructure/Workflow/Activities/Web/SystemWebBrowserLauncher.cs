namespace RPA.Infrastructure.Workflow.Activities.Web;

using System.Diagnostics;

public sealed class SystemWebBrowserLauncher : IWebBrowserLauncher
{
    public Task OpenAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Process.Start(new ProcessStartInfo
        {
            FileName = uri.AbsoluteUri,
            UseShellExecute = true,
        });

        return Task.CompletedTask;
    }
}

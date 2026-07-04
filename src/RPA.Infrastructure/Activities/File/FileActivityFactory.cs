namespace RPA.Infrastructure.Activities.File;

using RPA.Domain.Interfaces;

/// <summary>
/// File aktivitelerini üreten factory.
/// </summary>
public sealed class FileActivityFactory : IActivityFactory
{
    private static readonly Dictionary<string, Func<IActivity>> ActivityRegistry = new(StringComparer.OrdinalIgnoreCase)
    {
        { "File.Copy", () => new FileCopyActivity() },
        { "File.Move", () => new FileMoveActivity() },
        { "File.Delete", () => new FileDeleteActivity() },
        { "File.List", () => new FileListActivity() },
        { "File.Zip", () => new FileZipActivity() },
        { "File.Unzip", () => new FileUnzipActivity() }
    };

    public IActivity? CreateActivity(string activityId)
    {
        if (ActivityRegistry.TryGetValue(activityId, out var factory))
        {
            return factory();
        }

        return null;
    }
}

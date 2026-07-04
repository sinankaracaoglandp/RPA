namespace RPA.Infrastructure.Tests;

using RPA.Domain.Interfaces;
using RPA.Infrastructure.Activities.File;
using BusinessException = RPA.Domain.Exceptions.BusinessException;
using SystemException = RPA.Domain.Exceptions.SystemException;

/// <summary>
/// File Operations Testleri (Task 2.9.1). Spec Bölüm 5.3 — System.IO sarmalayıcıları.
/// FileCopyActivity, FileMoveActivity, FileDeleteActivity, FileListActivity, FileZipActivity, FileUnzipActivity
/// TDD: Tüm operasyonlar için success/error senaryoları, zip roundtrip.
/// </summary>
public class FileOperationsTests : IDisposable
{
    private readonly string _testDir;

    public FileOperationsTests()
    {
        _testDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rpa-file-tests-" + Guid.NewGuid().ToString("N")[..8]);
        System.IO.Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (System.IO.Directory.Exists(_testDir))
            System.IO.Directory.Delete(_testDir, recursive: true);
    }

    private IActivityExecutionContext CreateContext() => new TestActivityExecutionContext();

    private string CreateTestFile(string name, string content = "test content")
    {
        var path = System.IO.Path.Combine(_testDir, name);
        System.IO.File.WriteAllText(path, content);
        return path;
    }

    private string CreateTestDirectory(string name)
    {
        var path = System.IO.Path.Combine(_testDir, name);
        System.IO.Directory.CreateDirectory(path);
        return path;
    }

    // ================================================================ FileCopyActivity

    [Fact]
    public async Task FileCopy_Succeeds()
    {
        var sourceFile = CreateTestFile("source.txt", "test data");
        var destFile = System.IO.Path.Combine(_testDir, "destination.txt");
        var activity = new FileCopyActivity();
        var context = CreateContext();

        context.SetVariable("source", sourceFile);
        context.SetVariable("destination", destFile);
        context.SetVariable("overwrite", false);

        var result = await activity.ExecuteAsync(context);

        Assert.True(System.IO.File.Exists(destFile));
        Assert.Equal("test data", System.IO.File.ReadAllText(destFile));
    }

    [Fact]
    public async Task FileCopy_WithOverwrite()
    {
        var sourceFile = CreateTestFile("source.txt", "new content");
        var destFile = System.IO.Path.Combine(_testDir, "destination.txt");
        System.IO.File.WriteAllText(destFile, "old content");

        var activity = new FileCopyActivity();
        var context = CreateContext();
        context.SetVariable("source", sourceFile);
        context.SetVariable("destination", destFile);
        context.SetVariable("overwrite", true);

        await activity.ExecuteAsync(context);

        Assert.Equal("new content", System.IO.File.ReadAllText(destFile));
    }

    [Fact]
    public async Task FileCopy_FailsWithoutOverwrite()
    {
        var sourceFile = CreateTestFile("source.txt", "new");
        var destFile = System.IO.Path.Combine(_testDir, "destination.txt");
        System.IO.File.WriteAllText(destFile, "old");

        var activity = new FileCopyActivity();
        var context = CreateContext();
        context.SetVariable("source", sourceFile);
        context.SetVariable("destination", destFile);
        context.SetVariable("overwrite", false);

        await Assert.ThrowsAsync<SystemException>(() => activity.ExecuteAsync(context));
    }

    [Fact]
    public async Task FileCopy_MissingSourceThrows()
    {
        var activity = new FileCopyActivity();
        var context = CreateContext();
        context.SetVariable("source", "/nonexistent/file.txt");
        context.SetVariable("destination", System.IO.Path.Combine(_testDir, "dest.txt"));

        await Assert.ThrowsAsync<BusinessException>(() => activity.ExecuteAsync(context));
    }

    // ================================================================ FileMoveActivity

    [Fact]
    public async Task FileMove_Succeeds()
    {
        var sourceFile = CreateTestFile("source.txt", "test data");
        var destFile = System.IO.Path.Combine(_testDir, "destination.txt");
        var activity = new FileMoveActivity();
        var context = CreateContext();

        context.SetVariable("source", sourceFile);
        context.SetVariable("destination", destFile);

        await activity.ExecuteAsync(context);

        Assert.False(System.IO.File.Exists(sourceFile));
        Assert.True(System.IO.File.Exists(destFile));
        Assert.Equal("test data", System.IO.File.ReadAllText(destFile));
    }

    [Fact]
    public async Task FileMove_Rename()
    {
        var sourceFile = CreateTestFile("oldname.txt", "content");
        var destFile = System.IO.Path.Combine(_testDir, "newname.txt");
        var activity = new FileMoveActivity();
        var context = CreateContext();

        context.SetVariable("source", sourceFile);
        context.SetVariable("destination", destFile);

        await activity.ExecuteAsync(context);

        Assert.False(System.IO.File.Exists(sourceFile));
        Assert.True(System.IO.File.Exists(destFile));
    }

    [Fact]
    public async Task FileMove_MissingSourceThrows()
    {
        var activity = new FileMoveActivity();
        var context = CreateContext();
        context.SetVariable("source", "/nonexistent/file.txt");
        context.SetVariable("destination", System.IO.Path.Combine(_testDir, "dest.txt"));

        await Assert.ThrowsAsync<BusinessException>(() => activity.ExecuteAsync(context));
    }

    // ================================================================ FileDeleteActivity

    [Fact]
    public async Task FileDelete_Succeeds()
    {
        var fileToDelete = CreateTestFile("todelete.txt", "data");
        var activity = new FileDeleteActivity();
        var context = CreateContext();

        context.SetVariable("path", fileToDelete);

        await activity.ExecuteAsync(context);

        Assert.False(System.IO.File.Exists(fileToDelete));
    }

    [Fact]
    public async Task FileDelete_NonexistentThrows()
    {
        var activity = new FileDeleteActivity();
        var context = CreateContext();
        context.SetVariable("path", "/nonexistent/file.txt");

        await Assert.ThrowsAsync<BusinessException>(() => activity.ExecuteAsync(context));
    }

    // ================================================================ FileListActivity

    [Fact]
    public async Task FileList_ListsFiles()
    {
        CreateTestFile("file1.txt", "data1");
        CreateTestFile("file2.txt", "data2");
        CreateTestFile("other.doc", "data3");

        var activity = new FileListActivity();
        var context = CreateContext();
        context.SetVariable("folder", _testDir);
        context.SetVariable("pattern", "*");

        var result = await activity.ExecuteAsync(context);

        Assert.True(result.ContainsKey("files"));
    }

    [Fact]
    public async Task FileList_FiltersPattern()
    {
        CreateTestFile("file1.txt", "data1");
        CreateTestFile("file2.txt", "data2");
        CreateTestFile("readme.md", "data3");

        var activity = new FileListActivity();
        var context = CreateContext();
        context.SetVariable("folder", _testDir);
        context.SetVariable("pattern", "*.txt");

        var result = await activity.ExecuteAsync(context);

        Assert.True(result.ContainsKey("files"));
    }

    [Fact]
    public async Task FileList_NonexistentFolderThrows()
    {
        var activity = new FileListActivity();
        var context = CreateContext();
        context.SetVariable("folder", "/nonexistent/folder");
        context.SetVariable("pattern", "*");

        await Assert.ThrowsAsync<BusinessException>(() => activity.ExecuteAsync(context));
    }

    // ================================================================ FileZipActivity

    [Fact]
    public async Task FileZip_ZipsFile()
    {
        var sourceFile = CreateTestFile("file.txt", "content");
        var zipPath = System.IO.Path.Combine(_testDir, "archive.zip");
        var activity = new FileZipActivity();
        var context = CreateContext();

        context.SetVariable("source", sourceFile);
        context.SetVariable("zipPath", zipPath);

        var result = await activity.ExecuteAsync(context);

        Assert.True(System.IO.File.Exists(zipPath));
        Assert.Equal(zipPath, result["path"]);
    }

    [Fact]
    public async Task FileZip_ZipsDirectory()
    {
        var sourceDir = CreateTestDirectory("tozoip");
        CreateTestFile("tozoip/file1.txt", "content1");
        CreateTestFile("tozoip/file2.txt", "content2");

        var zipPath = System.IO.Path.Combine(_testDir, "archive.zip");
        var activity = new FileZipActivity();
        var context = CreateContext();

        context.SetVariable("source", sourceDir);
        context.SetVariable("zipPath", zipPath);

        var result = await activity.ExecuteAsync(context);

        Assert.True(System.IO.File.Exists(zipPath));
    }

    [Fact]
    public async Task FileZip_NonexistentSourceThrows()
    {
        var activity = new FileZipActivity();
        var context = CreateContext();
        context.SetVariable("source", "/nonexistent/file");
        context.SetVariable("zipPath", System.IO.Path.Combine(_testDir, "archive.zip"));

        await Assert.ThrowsAsync<BusinessException>(() => activity.ExecuteAsync(context));
    }

    // ================================================================ FileUnzipActivity

    [Fact]
    public async Task FileUnzip_Succeeds()
    {
        var sourceDir = CreateTestDirectory("tozip");
        CreateTestFile("tozip/file1.txt", "content1");
        CreateTestFile("tozip/file2.txt", "content2");

        var zipPath = System.IO.Path.Combine(_testDir, "archive.zip");
        var extractDir = CreateTestDirectory("extracted");

        System.IO.Compression.ZipFile.CreateFromDirectory(sourceDir, zipPath, System.IO.Compression.CompressionLevel.Optimal, includeBaseDirectory: false);

        var activity = new FileUnzipActivity();
        var context = CreateContext();
        context.SetVariable("zipPath", zipPath);
        context.SetVariable("targetFolder", extractDir);

        var result = await activity.ExecuteAsync(context);

        Assert.True(result.ContainsKey("files"));
        Assert.True(System.IO.File.Exists(System.IO.Path.Combine(extractDir, "file1.txt")));
        Assert.True(System.IO.File.Exists(System.IO.Path.Combine(extractDir, "file2.txt")));
    }

    [Fact]
    public async Task FileUnzip_NonexistentZipThrows()
    {
        var activity = new FileUnzipActivity();
        var context = CreateContext();
        context.SetVariable("zipPath", "/nonexistent/archive.zip");
        context.SetVariable("targetFolder", _testDir);

        await Assert.ThrowsAsync<BusinessException>(() => activity.ExecuteAsync(context));
    }

    // ================================================================ Roundtrip

    [Fact]
    public async Task ZipRoundtrip_FilesPreserved()
    {
        var sourceDir = CreateTestDirectory("roundtrip_source");
        CreateTestFile("roundtrip_source/file1.txt", "content1");
        CreateTestFile("roundtrip_source/file2.txt", "content2");

        var zipPath = System.IO.Path.Combine(_testDir, "roundtrip.zip");
        var extractDir = System.IO.Path.Combine(_testDir, "roundtrip_extract");

        // Zip
        var zipActivity = new FileZipActivity();
        var zipContext = CreateContext();
        zipContext.SetVariable("source", sourceDir);
        zipContext.SetVariable("zipPath", zipPath);
        await zipActivity.ExecuteAsync(zipContext);

        // Unzip
        var unzipActivity = new FileUnzipActivity();
        var unzipContext = CreateContext();
        unzipContext.SetVariable("zipPath", zipPath);
        unzipContext.SetVariable("targetFolder", extractDir);
        await unzipActivity.ExecuteAsync(unzipContext);

        // Verify
        Assert.True(System.IO.File.Exists(System.IO.Path.Combine(extractDir, "file1.txt")));
        Assert.True(System.IO.File.Exists(System.IO.Path.Combine(extractDir, "file2.txt")));
        Assert.Equal("content1", System.IO.File.ReadAllText(System.IO.Path.Combine(extractDir, "file1.txt")));
        Assert.Equal("content2", System.IO.File.ReadAllText(System.IO.Path.Combine(extractDir, "file2.txt")));
    }

    [Fact]
    public async Task CompleteWorkflow()
    {
        // Create → Copy → Move → List → Zip → Unzip → Delete
        var source = CreateTestFile("workflow_test.txt", "workflow content");
        var copy = System.IO.Path.Combine(_testDir, "copy.txt");
        var moved = System.IO.Path.Combine(_testDir, "moved.txt");
        var zipPath = System.IO.Path.Combine(_testDir, "moved.zip");
        var extractDir = CreateTestDirectory("extracted_workflow");

        // Copy
        var copyActivity = new FileCopyActivity();
        var copyContext = CreateContext();
        copyContext.SetVariable("source", source);
        copyContext.SetVariable("destination", copy);
        copyContext.SetVariable("overwrite", false);
        await copyActivity.ExecuteAsync(copyContext);
        Assert.True(System.IO.File.Exists(copy));

        // Move
        var moveActivity = new FileMoveActivity();
        var moveContext = CreateContext();
        moveContext.SetVariable("source", copy);
        moveContext.SetVariable("destination", moved);
        await moveActivity.ExecuteAsync(moveContext);
        Assert.True(System.IO.File.Exists(moved));

        // List
        var listActivity = new FileListActivity();
        var listContext = CreateContext();
        listContext.SetVariable("folder", _testDir);
        listContext.SetVariable("pattern", "*.txt");
        var listResult = await listActivity.ExecuteAsync(listContext);
        Assert.True(listResult.ContainsKey("files"));

        // Zip
        var zipActivity = new FileZipActivity();
        var zipContext = CreateContext();
        zipContext.SetVariable("source", moved);
        zipContext.SetVariable("zipPath", zipPath);
        await zipActivity.ExecuteAsync(zipContext);
        Assert.True(System.IO.File.Exists(zipPath));

        // Unzip
        var unzipActivity = new FileUnzipActivity();
        var unzipContext = CreateContext();
        unzipContext.SetVariable("zipPath", zipPath);
        unzipContext.SetVariable("targetFolder", extractDir);
        var unzipResult = await unzipActivity.ExecuteAsync(unzipContext);
        Assert.True(unzipResult.ContainsKey("files"));

        // Verify
        var extractedFile = System.IO.Path.Combine(extractDir, "moved.txt");
        Assert.True(System.IO.File.Exists(extractedFile));
        Assert.Equal("workflow content", System.IO.File.ReadAllText(extractedFile));

        // Delete
        var deleteActivity = new FileDeleteActivity();
        var deleteContext = CreateContext();
        deleteContext.SetVariable("path", moved);
        await deleteActivity.ExecuteAsync(deleteContext);
        Assert.False(System.IO.File.Exists(moved));
    }

    // ================================================================ Metadata

    [Fact]
    public void Metadata_FileCopy()
    {
        var activity = new FileCopyActivity();
        var metadata = activity.GetMetadata();
        Assert.Equal("File.Copy", metadata.ActivityId);
        Assert.Equal("Dosya Kopyala", metadata.DisplayName);
        Assert.Contains(metadata.Inputs, p => p.Name == "source");
    }

    [Fact]
    public void Metadata_FileZip()
    {
        var activity = new FileZipActivity();
        var metadata = activity.GetMetadata();
        Assert.Equal("File.Zip", metadata.ActivityId);
        Assert.Contains(metadata.Outputs, p => p.Name == "path");
    }

    [Fact]
    public void Metadata_FileUnzip()
    {
        var activity = new FileUnzipActivity();
        var metadata = activity.GetMetadata();
        Assert.Equal("File.Unzip", metadata.ActivityId);
        Assert.Contains(metadata.Outputs, p => p.Name == "files");
    }

    [Fact]
    public void AllActivities_HaveFileCapability()
    {
        var activities = new IActivity[]
        {
            new FileCopyActivity(),
            new FileMoveActivity(),
            new FileDeleteActivity(),
            new FileListActivity(),
            new FileZipActivity(),
            new FileUnzipActivity()
        };

        foreach (var activity in activities)
            Assert.Contains("file", activity.GetMetadata().RequiredCapabilities);
    }
}

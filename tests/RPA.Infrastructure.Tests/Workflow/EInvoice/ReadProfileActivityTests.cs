namespace RPA.Infrastructure.Tests.Workflow.EInvoice;

using Microsoft.EntityFrameworkCore;
using RPA.Application.EInvoiceProfiles;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Persistence;
using RPA.Infrastructure.Services;
using RPA.Infrastructure.Workflow.Activities.EInvoice;

public sealed class ReadProfileActivityTests
{
    [Fact]
    public async Task ReadProfile_PinsVersion_AndSetsRequestedRootVariable()
    {
        await using var db = Database();
        var (projectId, profileId) = await SeedProfileAsync(db);
        var activity = new ReadProfileActivity(Service(db), new EInvoiceProfileExtractor());
        var context = FakeActivityContext.With(
            ("projectId", projectId),
            ("profileId", profileId),
            ("profileVersion", 1),
            ("sourceMode", "XmlContent"),
            ("xmlContent", Invoice("FTR-1")),
            ("outputVariable", "fatura"));

        var outputs = await activity.ExecuteAsync(context);

        var invoice = Assert.IsType<Dictionary<string, object?>>(context.Variables["fatura"]);
        Assert.Equal("FTR-1", invoice["faturaNo"]);
        Assert.Same(invoice, outputs["fatura"]);
    }

    [Fact]
    public async Task ReadProfileBatch_Folder_DefaultsToTopDirectoryXmlFilesInStableOrder()
    {
        await using var db = Database();
        var (projectId, profileId) = await SeedProfileAsync(db);
        using var folder = new TemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(folder.Path, "b.xml"), Invoice("B"));
        await File.WriteAllTextAsync(Path.Combine(folder.Path, "a.xml"), Invoice("A"));
        await File.WriteAllTextAsync(Path.Combine(folder.Path, "skip.txt"), Invoice("X"));
        Directory.CreateDirectory(Path.Combine(folder.Path, "nested"));
        await File.WriteAllTextAsync(Path.Combine(folder.Path, "nested", "c.xml"), Invoice("C"));
        var activity = new ReadProfileBatchActivity(Service(db), new EInvoiceProfileExtractor());
        var context = FakeActivityContext.With(
            ("projectId", projectId),
            ("profileId", profileId),
            ("profileVersion", 1),
            ("sourceMode", "Folder"),
            ("folderPath", folder.Path),
            ("includeSubfolders", false),
            ("outputVariable", "faturalar"));

        await activity.ExecuteAsync(context);

        var results = Assert.IsType<List<Dictionary<string, object?>>>(context.Variables["faturalar"]);
        Assert.Equal(new[] { "A", "B" }, results.Select(item => item["faturaNo"]));
    }

    private static async Task<(Guid ProjectId, Guid ProfileId)> SeedProfileAsync(RpaDbContext db)
    {
        var project = new Project { Name = "P" };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var service = Service(db);
        var profile = await service.CreateAsync(project.Id, "Satis", null, default);
        await service.SaveDraftAsync(project.Id, profile.Id, Definition("faturaNo"), default);
        await service.PublishAsync(project.Id, profile.Id, null, default);
        return (project.Id, profile.Id);
    }

    private static EInvoiceProfileService Service(RpaDbContext db) =>
        new(db, new EInvoiceProfileDefinitionValidator());

    private static RpaDbContext Database()
    {
        var options = new DbContextOptionsBuilder<RpaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new RpaDbContext(options);
    }

    private sealed class FakeActivityContext : IActivityExecutionContext
    {
        public Dictionary<string, object?> Variables { get; } = new(StringComparer.Ordinal);
        public string TimeZone => "UTC";
        public Guid JobRunId { get; } = Guid.NewGuid();
        public static FakeActivityContext With(params (string Name, object? Value)[] variables)
        {
            var context = new FakeActivityContext();
            foreach (var (name, value) in variables) context.Variables[name] = value;
            return context;
        }

        public T GetVariable<T>(string name) =>
            Variables.TryGetValue(name, out var value) && value is not null ? (T)value : default!;

        public void SetVariable(string name, object? value) => Variables[name] = value;
        public Task<string> GetCredentialAsync(string credentialName) => Task.FromResult(string.Empty);
        public Task<string?> GetAssetAsync(string assetName) => Task.FromResult<string?>(null);
        public void Log(string message, LogLevel level = LogLevel.Information) { }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }

    private static string Definition(string name) =>
        $$"""{"fields":[{"name":"{{name}}","source":"XPath","valueXPath":"/inv:Invoice/cbc:ID","type":"string"}],"collections":[]}""";

    private static string Invoice(string number) =>
        $$"""
        <Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"
                 xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2">
          <cbc:ID>{{number}}</cbc:ID>
        </Invoice>
        """;
}

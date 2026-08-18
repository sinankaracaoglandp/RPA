namespace RPA.Infrastructure.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;
using RPA.Application.EInvoiceProfiles;
using RPA.Domain.Entities;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Persistence;
using RPA.Infrastructure.Services;
using RPA.Infrastructure.Workflow;
using RPA.Infrastructure.Workflow.Activities.EInvoice;
using BusinessException = RPA.Domain.Exceptions.BusinessException;
using SystemException = RPA.Domain.Exceptions.SystemException;

/// <summary>
/// BaseRunner state machine testleri (Task 2.2.1). Spec Bölüm 5.2.
/// TDD: doğrusal akış, dallanma, döngü, try/catch, scope izolasyonu, döngü tespiti.
/// </summary>
public class BaseRunnerTests
{
    private const string TwoLineUbl = """
        <Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"
                 xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2"
                 xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2">
          <cbc:ID>FTR202600008</cbc:ID>
          <cac:InvoiceLine><cbc:ID>1</cbc:ID><cac:Item><cac:SellersItemIdentification><cbc:ID>URUN-1</cbc:ID></cac:SellersItemIdentification><cbc:Name>Bir</cbc:Name></cac:Item></cac:InvoiceLine>
          <cac:InvoiceLine><cbc:ID>2</cbc:ID><cac:Item><cac:SellersItemIdentification><cbc:ID>URUN-2</cbc:ID></cac:SellersItemIdentification><cbc:Name>Iki</cbc:Name></cac:Item></cac:InvoiceLine>
        </Invoice>
        """;

    // ---------------------------------------------------------------- altyapı

    private static BaseRunner CreateRunner(
        IReadOnlyDictionary<string, ActivityMetadata>? catalog = null,
        IActivityFactory? factory = null,
        Func<string, string?, string?>? componentResolver = null,
        IWorkflowExecutionObserver? observer = null)
    {
        var cat = catalog is null ? new ActivityCatalog() : new ActivityCatalog(catalog);
        return new BaseRunner(
            new WorkflowValidator(),
            cat,
            factory ?? new EmptyActivityFactory(),
            NullLogger<BaseRunner>.Instance,
            vault: null,
            componentResolver: componentResolver,
            observer: observer);
    }

    private static WorkflowVersion Version(string json) => new() { JsonDefinition = json };

    private static ActivityMetadata Meta(string id) =>
        new() { ActivityId = id, DisplayName = id };

    private static RpaDbContext CreateEInvoiceProfileDatabase()
    {
        var options = new DbContextOptionsBuilder<RpaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new RpaDbContext(options);
    }

    private static async Task<(Guid ProjectId, Guid ProfileId)> SeedPublishedEInvoiceProfileAsync(
        RpaDbContext db,
        string definitionJson)
    {
        var project = new Project { Name = "E-Fatura Test Projesi" };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var service = new EInvoiceProfileService(db, new EInvoiceProfileDefinitionValidator());
        var profile = await service.CreateAsync(project.Id, "Satis", null, default);
        await service.SaveDraftAsync(project.Id, profile.Id, definitionJson, default);
        await service.PublishAsync(project.Id, profile.Id, null, default);
        return (project.Id, profile.Id);
    }

    private static string DynamicLineProfileDefinition() =>
        """
        {
          "fields":[
            {"name":"faturaNo","source":"XPath","valueXPath":"/inv:Invoice/cbc:ID","type":"string"}
          ],
          "collections":[
            {
              "name":"satirlar",
              "scopeXPath":"//cac:InvoiceLine",
              "fields":[
                {"name":"MalzemeKodu","source":"XPath","valueXPath":"cac:Item/cac:SellersItemIdentification/cbc:ID","type":"string"},
                {"name":"Aciklama","source":"XPath","valueXPath":"cac:Item/cbc:Name","type":"string"}
              ]
            }
          ]
        }
        """;

    private static string EscapeJsonStringContent(string value) =>
        System.Text.Json.JsonSerializer.Serialize(value)[1..^1];

    /// <summary>Yapılandırılabilir sahte aktivite.</summary>
    private sealed class FakeActivity : IActivity
    {
        private readonly Func<IActivityExecutionContext, Task<Dictionary<string, object?>>> _fn;
        private readonly ActivityMetadata _meta;

        public FakeActivity(string id, Func<IActivityExecutionContext, Task<Dictionary<string, object?>>> fn)
        {
            _meta = new ActivityMetadata { ActivityId = id, DisplayName = id };
            _fn = fn;
        }

        public Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context) => _fn(context);
        public ActivityMetadata GetMetadata() => _meta;
    }

    private sealed class MapFactory : IActivityFactory
    {
        private readonly Dictionary<string, Func<IActivity>> _map = new();
        public MapFactory Add(string id, Func<IActivity> f) { _map[id] = f; return this; }
        public IActivity? CreateActivity(string activityId) =>
            _map.TryGetValue(activityId, out var f) ? f() : null;
    }

    // ---------------------------------------------------------------- testler

    [Fact]
    public async Task LinearFlow_AssignThenLog_ProducesOutput()
    {
        var json = """
        {
          "schemaVersion": "1.0",
          "id": "11111111-1111-1111-1111-111111111111",
          "name": "Doğrusal",
          "version": "1.0.0",
          "arguments": { "in": [ { "name": "name", "type": "string" } ],
                         "out": [ { "name": "message", "type": "string" } ] },
          "nodes": [
            { "id": "n1", "type": "assign", "variableName": "message", "value": "Merhaba ${name}" },
            { "id": "n2", "type": "log", "message": "log: ${message}" }
          ],
          "connections": [ { "from": "n1", "to": "n2" } ]
        }
        """;

        var runner = CreateRunner();
        var result = await runner.ExecuteAsync(
            Version(json), new() { ["name"] = "Dünya" }, Guid.NewGuid());

        Assert.True(result.Success, result.Exception?.Message);
        Assert.Equal("Merhaba Dünya", result.Outputs["message"]);
    }

    [Fact]
    public async Task If_TrueBranch_Taken()
    {
        var result = await RunIf(nValue: 10);
        Assert.Equal("big", result.Outputs["result"]);
    }

    [Fact]
    public async Task If_FalseBranch_Taken()
    {
        var result = await RunIf(nValue: 3);
        Assert.Equal("small", result.Outputs["result"]);
    }

    private static async Task<WorkflowExecutionResult> RunIf(int nValue)
    {
        var json = """
        {
          "schemaVersion": "1.0",
          "id": "22222222-2222-2222-2222-222222222222",
          "name": "Dallanma",
          "version": "1.0.0",
          "arguments": { "in": [ { "name": "n", "type": "int" } ],
                         "out": [ { "name": "result", "type": "string" } ] },
          "nodes": [
            { "id": "if1", "type": "if", "condition": "${n} > 5" },
            { "id": "big", "type": "assign", "variableName": "result", "value": "big" },
            { "id": "small", "type": "assign", "variableName": "result", "value": "small" }
          ],
          "connections": [
            { "from": "if1", "to": "big", "fromPort": "true" },
            { "from": "if1", "to": "small", "fromPort": "false" }
          ]
        }
        """;
        var runner = CreateRunner();
        var result = await runner.ExecuteAsync(Version(json), new() { ["n"] = nValue }, Guid.NewGuid());
        Assert.True(result.Success, result.Exception?.Message);
        return result;
    }

    [Fact]
    public async Task ForEach_IteratesEachItem()
    {
        var seen = new List<object?>();
        var factory = new MapFactory().Add("Test.Record",
            () => new FakeActivity("Test.Record", ctx =>
            {
                seen.Add(ctx.GetVariable<long>("value"));
                return Task.FromResult(new Dictionary<string, object?>());
            }));

        var json = """
        {
          "schemaVersion": "1.0",
          "id": "33333333-3333-3333-3333-333333333333",
          "name": "Döngü",
          "version": "1.0.0",
          "variables": [ { "name": "nums", "type": "JSON", "default": [1, 2, 3] } ],
          "nodes": [
            { "id": "fe", "type": "forEach", "items": "nums", "itemVariable": "x",
              "bodyStartNodeId": "body", "bodyEndNodeId": "body" },
            { "id": "body", "type": "activity", "activity": "Test.Record",
              "properties": { "value": "${x}" } }
          ],
          "connections": []
        }
        """;

        var runner = CreateRunner(
            catalog: new Dictionary<string, ActivityMetadata> { ["Test.Record"] = Meta("Test.Record") },
            factory: factory);
        var result = await runner.ExecuteAsync(Version(json), new(), Guid.NewGuid());

        Assert.True(result.Success, result.Exception?.Message);
        Assert.Equal(3, seen.Count);
        Assert.Equal(new object?[] { 1L, 2L, 3L }, seen);
    }

    [Fact]
    public async Task Runner_ReadUblOutputsFeedForEachAndDownstreamActivity()
    {
        var seen = new List<string?>();
        var factory = new MapFactory()
            .Add("EInvoice.ReadUbl", () => new ReadUblActivity(new UblInvoiceParser()))
            .Add("Test.RecordProductCode", () => new FakeActivity("Test.RecordProductCode", context =>
            {
                seen.Add(context.GetVariable<string?>("productCode"));
                return Task.FromResult(new Dictionary<string, object?>());
            }));

        var json = """
        {
          "schemaVersion":"1.0","id":"34343434-3434-3434-3434-343434343434",
          "name":"UBL satirlarini isle","version":"1.0.0",
          "nodes":[
            {"id":"read","type":"activity","activity":"EInvoice.ReadUbl",
             "properties":{"xmlContent":"__XML__","outputBindings":{"lines":"invoiceLines"}}},
            {"id":"fe","type":"forEach","items":"{{invoiceLines}}","itemVariable":"line"},
            {"id":"record","type":"activity","activity":"Test.RecordProductCode",
             "properties":{"productCode":"{{line.itemCode}}"}}
          ],
          "connections":[
            {"from":"read","to":"fe"},
            {"from":"fe","fromPort":"body","to":"record"},
            {"from":"record","to":"fe","toPort":"loop-back"}
          ]
        }
        """.Replace("\"__XML__\"", System.Text.Json.JsonSerializer.Serialize(TwoLineUbl));

        var catalog = new Dictionary<string, ActivityMetadata>
        {
            ["EInvoice.ReadUbl"] = new ReadUblActivity(new UblInvoiceParser()).GetMetadata(),
            ["Test.RecordProductCode"] = Meta("Test.RecordProductCode"),
        };
        var result = await CreateRunner(catalog, factory)
            .ExecuteAsync(Version(json), new(), Guid.NewGuid());

        Assert.True(result.Success, result.Exception?.Message);
        Assert.Equal(new[] { "URUN-1", "URUN-2" }, seen);
    }

    [Theory]
    [InlineData("<Invoice xmlns='urn:oasis:names:specification:ubl:schema:xsd:Invoice-2'><ID>SECRET-SUCCESS</ID></Invoice>")]
    [InlineData("<broken>SECRET-ERROR")]
    public async Task Runner_EInvoiceObserverNeverReceivesXmlContent(string xml)
    {
        var observer = new RecordingObserver();
        var factory = new MapFactory().Add("EInvoice.ReadUbl", () => new ReadUblActivity(new UblInvoiceParser()));
        var json = """
        {"schemaVersion":"1.0","id":"35353535-3535-3535-3535-353535353535","name":"mask","version":"1.0.0",
         "nodes":[{"id":"read","type":"activity","activity":"EInvoice.ReadUbl","properties":{"xmlContent":"__XML__"}}],"connections":[]}
        """.Replace("\"__XML__\"", System.Text.Json.JsonSerializer.Serialize(xml));

        await CreateRunner(
            ActivityRegistry.BuildCatalog(),
            factory,
            observer: observer).ExecuteAsync(Version(json), new(), Guid.NewGuid());

        var completed = Assert.Single(observer.Completed);
        Assert.Equal("[MASKED]", completed.Inputs!["xmlContent"]);
        Assert.DoesNotContain("SECRET", string.Join("|", completed.Inputs.Values.Concat(completed.Outputs?.Values ?? []).Append(completed.Error)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Observer_ReportsVariableSnapshot_ForActivityAndNonActivityNodes()
    {
        var observer = new RecordingObserver();
        var factory = new MapFactory().Add("Test.Rows", () => new FakeActivity("Test.Rows", _ =>
            Task.FromResult(new Dictionary<string, object?>
            {
                ["gridSatirlari"] = new List<Dictionary<string, object?>>
                {
                    new() { ["MATNR"] = "M-1", ["MAKTX"] = "Vida" },
                    new() { ["MATNR"] = "M-2", ["MAKTX"] = "Somun" },
                },
            })));

        var json = """
        {"schemaVersion":"1.0","id":"37373737-3737-3737-3737-373737373737","name":"snapshot","version":"1.0.0",
         "nodes":[
           {"id":"n1","type":"assign","variableName":"sayac","value":"7"},
           {"id":"n2","type":"activity","activity":"Test.Rows","properties":{}}
         ],
         "connections":[{"from":"n1","to":"n2"}]}
        """;

        var result = await CreateRunner(
            new Dictionary<string, ActivityMetadata>
            {
                ["Test.Rows"] = new() { ActivityId = "Test.Rows", DisplayName = "Test.Rows" },
            },
            factory,
            observer: observer).ExecuteAsync(Version(json), new(), Guid.NewGuid());

        Assert.True(result.Success, result.Exception?.Message);

        // assign gibi aktivite olmayan node'lar da tamamlanma olayı yayınlar (değişken görünürlüğü).
        var assignEvent = Assert.Single(observer.Completed, e => e.NodeId == "n1");
        Assert.Equal("7", assignEvent.Variables!["sayac"]);

        // list<object> çıktısı anlık görüntüde JSON olarak, alan değerleriyle birlikte görünür.
        var activityEvent = Assert.Single(observer.Completed, e => e.NodeId == "n2");
        Assert.Equal("7", activityEvent.Variables!["sayac"]);
        Assert.Contains("MAKTX", activityEvent.Variables["gridSatirlari"]!, StringComparison.Ordinal);
        Assert.Contains("Somun", activityEvent.Variables["gridSatirlari"]!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Observer_VariableSnapshot_MasksSensitiveValues_InLaterNodesToo()
    {
        var observer = new RecordingObserver();
        var factory = new MapFactory()
            .Add("Test.Secret", () => new FakeActivity("Test.Secret", _ =>
                Task.FromResult(new Dictionary<string, object?> { ["apiKey"] = "SECRET-VALUE" })))
            .Add("Test.Noop", () => new FakeActivity("Test.Noop", _ =>
                Task.FromResult(new Dictionary<string, object?>())));

        var json = """
        {"schemaVersion":"1.0","id":"38383838-3838-3838-3838-383838383838","name":"secret","version":"1.0.0",
         "nodes":[
           {"id":"s1","type":"activity","activity":"Test.Secret","properties":{}},
           {"id":"s2","type":"activity","activity":"Test.Noop","properties":{}}
         ],
         "connections":[{"from":"s1","to":"s2"}]}
        """;

        await CreateRunner(
            new Dictionary<string, ActivityMetadata>
            {
                ["Test.Secret"] = new()
                {
                    ActivityId = "Test.Secret",
                    DisplayName = "Test.Secret",
                    Outputs = [new ActivityParameter { Name = "apiKey", Type = "Sensitive" }],
                },
                ["Test.Noop"] = new() { ActivityId = "Test.Noop", DisplayName = "Test.Noop" },
            },
            factory,
            observer: observer).ExecuteAsync(Version(json), new(), Guid.NewGuid());

        foreach (var evt in observer.Completed)
        {
            Assert.Equal("[MASKED]", evt.Variables!["apiKey"]);
        }
    }

    [Fact]
    public async Task PublishedProfile_XmlList_ForEach_ExposesDynamicLineFields()
    {
        await using var db = CreateEInvoiceProfileDatabase();
        var (projectId, profileId) = await SeedPublishedEInvoiceProfileAsync(db, DynamicLineProfileDefinition());
        var profileService = new EInvoiceProfileService(db, new EInvoiceProfileDefinitionValidator());
        var seen = new List<string?>();
        var factory = new MapFactory()
            .Add("EInvoice.ReadProfileBatch", () => new ReadProfileBatchActivity(profileService, new EInvoiceProfileExtractor()))
            .Add("Test.RecordProductCode", () => new FakeActivity("Test.RecordProductCode", context =>
            {
                seen.Add(context.GetVariable<string?>("productCode"));
                return Task.FromResult(new Dictionary<string, object?>());
            }));

        var json = """
        {
          "schemaVersion":"1.0","id":"36363636-3636-3636-3636-363636363636",
          "name":"Profil satirlarini isle","version":"1.0.0",
          "nodes":[
            {"id":"read","type":"activity","activity":"EInvoice.ReadProfileBatch",
             "properties":{
               "projectId":"__PROJECT_ID__",
               "profileId":"__PROFILE_ID__",
               "profileVersion":1,
               "sourceMode":"XmlContents",
               "xmlContents":["__XML__"],
               "outputVariable":"faturalar"
             }},
            {"id":"eachInvoice","type":"forEach","items":"{{faturalar}}","itemVariable":"fatura"},
            {"id":"eachLine","type":"forEach","items":"{{fatura.satirlar}}","itemVariable":"satir"},
            {"id":"record","type":"activity","activity":"Test.RecordProductCode",
             "properties":{"productCode":"{{satir.MalzemeKodu}}"}}
          ],
          "connections":[
            {"from":"read","to":"eachInvoice"},
            {"from":"eachInvoice","fromPort":"body","to":"eachLine"},
            {"from":"eachLine","fromPort":"body","to":"record"},
            {"from":"record","to":"eachLine","toPort":"loop-back"},
            {"from":"eachLine","to":"eachInvoice","toPort":"loop-back"}
          ]
        }
        """
        .Replace("__PROJECT_ID__", projectId.ToString())
        .Replace("__PROFILE_ID__", profileId.ToString())
        .Replace("__XML__", EscapeJsonStringContent(TwoLineUbl));

        var catalog = new Dictionary<string, ActivityMetadata>
        {
            ["EInvoice.ReadProfileBatch"] = new ReadProfileBatchActivity(profileService, new EInvoiceProfileExtractor()).GetMetadata(),
            ["Test.RecordProductCode"] = Meta("Test.RecordProductCode"),
        };

        var result = await CreateRunner(catalog, factory)
            .ExecuteAsync(Version(json), new(), Guid.NewGuid());

        Assert.True(result.Success, result.Exception?.Message);
        Assert.Equal(new[] { "URUN-1", "URUN-2" }, seen);
    }

    [Fact]
    public async Task ProfileNodes_NeverExposeXmlToObserverOrLogs()
    {
        await using var db = CreateEInvoiceProfileDatabase();
        var (projectId, profileId) = await SeedPublishedEInvoiceProfileAsync(db, DynamicLineProfileDefinition());
        var profileService = new EInvoiceProfileService(db, new EInvoiceProfileDefinitionValidator());
        var observer = new RecordingObserver();
        var xml = """
        <Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"
                 xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2"
                 xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2">
          <cbc:ID>FTR-SECRET-SAFE</cbc:ID>
          <cbc:Note>SECRET-XML-SHOULD-NOT-LEAK</cbc:Note>
        </Invoice>
        """;
        var factory = new MapFactory()
            .Add("EInvoice.ReadProfile", () => new ReadProfileActivity(profileService, new EInvoiceProfileExtractor()));
        var json = """
        {
          "schemaVersion":"1.0","id":"37373737-3737-3737-3737-373737373737",
          "name":"Profil kaynak maskeleme","version":"1.0.0",
          "nodes":[
            {"id":"read","type":"activity","activity":"EInvoice.ReadProfile",
             "properties":{
               "projectId":"__PROJECT_ID__",
               "profileId":"__PROFILE_ID__",
               "profileVersion":1,
               "sourceMode":"XmlContent",
               "xmlContent":"__XML__",
               "outputVariable":"fatura"
             }}
          ],
          "connections":[]
        }
        """
        .Replace("__PROJECT_ID__", projectId.ToString())
        .Replace("__PROFILE_ID__", profileId.ToString())
        .Replace("__XML__", EscapeJsonStringContent(xml));

        await CreateRunner(
            ActivityRegistry.BuildCatalog(),
            factory,
            observer: observer).ExecuteAsync(Version(json), new(), Guid.NewGuid());

        var completed = Assert.Single(observer.Completed);
        Assert.Equal("[MASKED]", completed.Inputs!["xmlContent"]);
        var observed = string.Join("|", completed.Inputs.Values.Concat(completed.Outputs?.Values ?? []).Append(completed.Error));
        Assert.DoesNotContain("SECRET-XML-SHOULD-NOT-LEAK", observed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task While_LoopsUntilConditionFalse()
    {
        var factory = new MapFactory().Add("Test.Countdown",
            () => new FakeActivity("Test.Countdown", ctx =>
            {
                var c = ctx.GetVariable<long>("counter");
                return Task.FromResult(new Dictionary<string, object?> { ["counter"] = c - 1 });
            }));

        var json = """
        {
          "schemaVersion": "1.0",
          "id": "44444444-4444-4444-4444-444444444444",
          "name": "While",
          "version": "1.0.0",
          "variables": [ { "name": "counter", "type": "int", "default": 3 } ],
          "arguments": { "out": [ { "name": "counter", "type": "int" } ] },
          "nodes": [
            { "id": "w", "type": "while", "condition": "${counter} > 0",
              "bodyStartNodeId": "body", "bodyEndNodeId": "body" },
            { "id": "body", "type": "activity", "activity": "Test.Countdown", "properties": {} }
          ],
          "connections": []
        }
        """;

        var runner = CreateRunner(
            catalog: new Dictionary<string, ActivityMetadata> { ["Test.Countdown"] = Meta("Test.Countdown") },
            factory: factory);
        var result = await runner.ExecuteAsync(Version(json), new(), Guid.NewGuid());

        Assert.True(result.Success, result.Exception?.Message);
        Assert.Equal(0L, Convert.ToInt64(result.Outputs["counter"]));
    }

    [Fact]
    public async Task For_IncludesEndValue_AndPublishesIndexVariable()
    {
        var seen = new List<long>();
        var factory = new MapFactory().Add("Test.RecordIndex",
            () => new FakeActivity("Test.RecordIndex", ctx =>
            {
                seen.Add(ctx.GetVariable<long>("index"));
                return Task.FromResult(new Dictionary<string, object?>());
            }));

        var json = """
        {
          "schemaVersion": "1.0",
          "id": "45454545-4545-4545-4545-454545454545",
          "name": "For",
          "version": "1.0.0",
          "nodes": [
            { "id": "f", "type": "for", "start": 1, "end": 3, "step": 1,
              "indexVariable": "index", "bodyStartNodeId": "body", "bodyEndNodeId": "body" },
            { "id": "body", "type": "activity", "activity": "Test.RecordIndex", "properties": {} }
          ],
          "connections": []
        }
        """;

        var runner = CreateRunner(
            catalog: new Dictionary<string, ActivityMetadata> { ["Test.RecordIndex"] = Meta("Test.RecordIndex") },
            factory: factory);
        var result = await runner.ExecuteAsync(Version(json), new(), Guid.NewGuid());

        Assert.True(result.Success, result.Exception?.Message);
        Assert.Equal(new long[] { 1, 2, 3 }, seen);
    }

    [Fact]
    public async Task For_UsesBodyLoopBackAndExitConnections()
    {
        var seen = new List<long>();
        var exitCount = 0;
        var factory = new MapFactory()
            .Add("Test.RecordIndex", () => new FakeActivity("Test.RecordIndex", ctx =>
            {
                seen.Add(ctx.GetVariable<long>("index"));
                return Task.FromResult(new Dictionary<string, object?>());
            }))
            .Add("Test.Exit", () => new FakeActivity("Test.Exit", _ =>
            {
                exitCount++;
                return Task.FromResult(new Dictionary<string, object?>());
            }));

        var json = """
        {
          "schemaVersion": "1.0",
          "id": "46464646-4646-4646-4646-464646464646",
          "name": "Connected For",
          "version": "1.0.0",
          "nodes": [
            { "id": "f", "type": "for", "start": 1, "end": 2, "step": 1, "indexVariable": "index" },
            { "id": "body", "type": "activity", "activity": "Test.RecordIndex", "properties": {} },
            { "id": "after", "type": "activity", "activity": "Test.Exit", "properties": {} }
          ],
          "connections": [
            { "from": "f", "fromPort": "body", "to": "body" },
            { "from": "body", "to": "f", "toPort": "loop-back" },
            { "from": "f", "fromPort": "exit", "to": "after" }
          ]
        }
        """;

        var catalog = new Dictionary<string, ActivityMetadata>
        {
            ["Test.RecordIndex"] = Meta("Test.RecordIndex"),
            ["Test.Exit"] = Meta("Test.Exit"),
        };
        var result = await CreateRunner(catalog: catalog, factory: factory)
            .ExecuteAsync(Version(json), new(), Guid.NewGuid());

        Assert.True(result.Success, result.Exception?.Message);
        Assert.Equal(new long[] { 1, 2 }, seen);
        Assert.Equal(1, exitCount);
    }

    [Fact]
    public async Task ForEach_UsesBodyLoopBackConnections()
    {
        var seen = new List<long>();
        var factory = new MapFactory().Add("Test.Record", () => new FakeActivity("Test.Record", ctx =>
        {
            seen.Add(ctx.GetVariable<long>("item"));
            return Task.FromResult(new Dictionary<string, object?>());
        }));
        var json = """
        {
          "schemaVersion":"1.0","id":"47474747-4747-4747-4747-474747474747","name":"FE","version":"1.0.0",
          "variables":[{"name":"items","type":"JSON","default":[1,2]}],
          "nodes":[
            {"id":"fe","type":"forEach","items":"items","itemVariable":"item"},
            {"id":"body","type":"activity","activity":"Test.Record","properties":{}}
          ],
          "connections":[
            {"from":"fe","fromPort":"body","to":"body"},
            {"from":"body","to":"fe","toPort":"loop-back"}
          ]
        }
        """;
        var result = await CreateRunner(
            catalog: new Dictionary<string, ActivityMetadata> { ["Test.Record"] = Meta("Test.Record") },
            factory: factory).ExecuteAsync(Version(json), new(), Guid.NewGuid());
        Assert.True(result.Success, result.Exception?.Message);
        Assert.Equal(new long[] { 1, 2 }, seen);
    }

    [Fact]
    public async Task FakeLoopBackCannotHideNormalGraphCycle()
    {
        var json = """
        {
          "schemaVersion":"1.0","id":"48484848-4848-4848-4848-484848484848","name":"Invalid","version":"1.0.0",
          "nodes":[{"id":"a","type":"log","message":"a"},{"id":"b","type":"log","message":"b"}],
          "connections":[
            {"from":"a","to":"b"},
            {"from":"b","to":"a","toPort":"loop-back"}
          ]
        }
        """;
        var result = await CreateRunner().ExecuteAsync(Version(json), new(), Guid.NewGuid());
        Assert.False(result.Success);
        Assert.Contains("loop-back", result.Exception?.Message);
    }

    [Fact]
    public async Task TryCatch_SystemException_RoutesToCatchBranch()
    {
        var factory = new MapFactory().Add("Test.Throw",
            () => new FakeActivity("Test.Throw",
                _ => throw new SystemException("bağlantı koptu")));

        var json = """
        {
          "schemaVersion": "1.0",
          "id": "55555555-5555-5555-5555-555555555555",
          "name": "TryCatch",
          "version": "1.0.0",
          "arguments": { "out": [ { "name": "result", "type": "string" },
                                  { "name": "err", "type": "string" } ] },
          "nodes": [
            { "id": "tc", "type": "tryCatch", "tryNodeId": "boom", "bodyEndNodeId": "boom",
              "catchNodeId": "handle", "exceptionVariable": "err" },
            { "id": "boom", "type": "activity", "activity": "Test.Throw", "properties": {} },
            { "id": "handle", "type": "assign", "variableName": "result", "value": "caught" }
          ],
          "connections": []
        }
        """;

        var runner = CreateRunner(
            catalog: new Dictionary<string, ActivityMetadata> { ["Test.Throw"] = Meta("Test.Throw") },
            factory: factory);
        var result = await runner.ExecuteAsync(Version(json), new(), Guid.NewGuid());

        Assert.True(result.Success, result.Exception?.Message);
        Assert.Equal("caught", result.Outputs["result"]);
        Assert.Equal("bağlantı koptu", result.Outputs["err"]);
    }

    [Fact]
    public async Task TryCatch_FinallyAlwaysRuns()
    {
        var factory = new MapFactory().Add("Test.Throw",
            () => new FakeActivity("Test.Throw", _ => throw new SystemException("hata")));

        var json = """
        {
          "schemaVersion": "1.0",
          "id": "66666666-6666-6666-6666-666666666666",
          "name": "Finally",
          "version": "1.0.0",
          "arguments": { "out": [ { "name": "cleanup", "type": "string" } ] },
          "nodes": [
            { "id": "tc", "type": "tryCatch", "tryNodeId": "boom", "bodyEndNodeId": "boom",
              "catchNodeId": "handle", "finallyNodeId": "fin" },
            { "id": "boom", "type": "activity", "activity": "Test.Throw", "properties": {} },
            { "id": "handle", "type": "log", "message": "yakalandı" },
            { "id": "fin", "type": "assign", "variableName": "cleanup", "value": "done" }
          ],
          "connections": []
        }
        """;

        var runner = CreateRunner(
            catalog: new Dictionary<string, ActivityMetadata> { ["Test.Throw"] = Meta("Test.Throw") },
            factory: factory);
        var result = await runner.ExecuteAsync(Version(json), new(), Guid.NewGuid());

        Assert.True(result.Success, result.Exception?.Message);
        Assert.Equal("done", result.Outputs["cleanup"]);
    }

    [Fact]
    public async Task Activity_OutputStoredAsVariable()
    {
        var factory = new MapFactory().Add("Test.Produce",
            () => new FakeActivity("Test.Produce",
                _ => Task.FromResult(new Dictionary<string, object?> { ["data"] = "42" })));

        var json = """
        {
          "schemaVersion": "1.0",
          "id": "77777777-7777-7777-7777-777777777777",
          "name": "Aktivite",
          "version": "1.0.0",
          "arguments": { "out": [ { "name": "data", "type": "string" } ] },
          "nodes": [ { "id": "a", "type": "activity", "activity": "Test.Produce", "properties": {} } ],
          "connections": []
        }
        """;

        var runner = CreateRunner(
            catalog: new Dictionary<string, ActivityMetadata> { ["Test.Produce"] = Meta("Test.Produce") },
            factory: factory);
        var result = await runner.ExecuteAsync(Version(json), new(), Guid.NewGuid());

        Assert.True(result.Success, result.Exception?.Message);
        Assert.Equal("42", result.Outputs["data"]);
    }

    [Fact]
    public async Task Activity_NotRegistered_FailsWithSystemException()
    {
        var json = """
        {
          "schemaVersion": "1.0",
          "id": "88888888-8888-8888-8888-888888888888",
          "name": "Eksik",
          "version": "1.0.0",
          "nodes": [ { "id": "a", "type": "activity", "activity": "Logic.Log", "properties": {} } ],
          "connections": []
        }
        """;

        var runner = CreateRunner(); // EmptyActivityFactory → null
        var result = await runner.ExecuteAsync(Version(json), new(), Guid.NewGuid());

        Assert.False(result.Success);
        Assert.IsType<SystemException>(result.Exception);
    }

    [Fact]
    public async Task Terminate_BusinessException_Propagates()
    {
        var json = """
        {
          "schemaVersion": "1.0",
          "id": "99999999-9999-9999-9999-999999999999",
          "name": "Terminate",
          "version": "1.0.0",
          "nodes": [ { "id": "t", "type": "terminate", "exceptionType": "Business",
                       "message": "iş kuralı" } ],
          "connections": []
        }
        """;

        var runner = CreateRunner();
        var result = await runner.ExecuteAsync(Version(json), new(), Guid.NewGuid());

        Assert.False(result.Success);
        Assert.IsType<BusinessException>(result.Exception);
        Assert.Equal("iş kuralı", result.Exception!.Message);
    }

    [Fact]
    public async Task CircularGraph_DetectedBeforeExecution()
    {
        var json = """
        {
          "schemaVersion": "1.0",
          "id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
          "name": "Döngüsel",
          "version": "1.0.0",
          "nodes": [
            { "id": "n1", "type": "log", "message": "a" },
            { "id": "n2", "type": "log", "message": "b" }
          ],
          "connections": [
            { "from": "n1", "to": "n2" },
            { "from": "n2", "to": "n1" }
          ]
        }
        """;

        var runner = CreateRunner();
        var result = await runner.ExecuteAsync(Version(json), new(), Guid.NewGuid());

        Assert.False(result.Success);
        Assert.IsType<SystemException>(result.Exception);
        Assert.Contains("döngü", result.Exception!.Message);
    }

    [Fact]
    public async Task InvalidNodeType_FailsSchemaValidation()
    {
        var json = """
        {
          "schemaVersion": "1.0",
          "id": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
          "name": "Geçersiz",
          "version": "1.0.0",
          "nodes": [ { "id": "n1", "type": "bogusType" } ],
          "connections": []
        }
        """;

        var runner = CreateRunner();
        var result = await runner.ExecuteAsync(Version(json), new(), Guid.NewGuid());

        Assert.False(result.Success);
        Assert.IsType<SystemException>(result.Exception);
    }

    [Fact]
    public async Task MalformedJson_FailsBeforeExecution()
    {
        var runner = CreateRunner();
        var result = await runner.ExecuteAsync(Version("{ this is not json"), new(), Guid.NewGuid());

        Assert.False(result.Success);
        Assert.IsType<SystemException>(result.Exception);
    }

    [Fact]
    public async Task Component_InvokedWithIsolatedScope_ReturnsMappedOutput()
    {
        var componentJson = """
        {
          "schemaVersion": "1.0",
          "id": "cccccccc-cccc-cccc-cccc-cccccccccccc",
          "name": "Bileşen",
          "version": "1.0.0",
          "arguments": { "in": [ { "name": "greeting", "type": "string" } ],
                         "out": [ { "name": "shout", "type": "string" } ] },
          "nodes": [ { "id": "n1", "type": "assign", "variableName": "shout", "value": "${greeting}!" } ],
          "connections": []
        }
        """;

        var runner = CreateRunner();
        var outputs = await runner.InvokeComponentAsync(
            new ComponentVersion { JsonDefinition = componentJson },
            new() { ["greeting"] = "selam" },
            Guid.NewGuid());

        Assert.Equal("selam!", outputs["shout"]);
    }

    [Fact]
    public async Task ComponentCall_Node_IsolatesAndMapsVariables()
    {
        var componentJson = """
        {
          "schemaVersion": "1.0",
          "id": "dddddddd-dddd-dddd-dddd-dddddddddddd",
          "name": "AltBileşen",
          "version": "1.0.0",
          "arguments": { "in": [ { "name": "input", "type": "string" } ],
                         "out": [ { "name": "output", "type": "string" } ] },
          "nodes": [ { "id": "n1", "type": "assign", "variableName": "output", "value": "islenmis-${input}" } ],
          "connections": []
        }
        """;

        var json = """
        {
          "schemaVersion": "1.0",
          "id": "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
          "name": "AnaAkış",
          "version": "1.0.0",
          "variables": [ { "name": "source", "type": "string", "default": "veri" } ],
          "arguments": { "out": [ { "name": "final", "type": "string" } ] },
          "nodes": [
            { "id": "cc", "type": "componentCall", "componentId": "dddddddd-dddd-dddd-dddd-dddddddddddd",
              "componentVersion": "1.0.0",
              "inputMapping": { "input": "source" },
              "outputMapping": { "output": "final" } }
          ],
          "connections": []
        }
        """;

        var runner = CreateRunner(componentResolver: (id, ver) => componentJson);
        var result = await runner.ExecuteAsync(Version(json), new(), Guid.NewGuid());

        Assert.True(result.Success, result.Exception?.Message);
        Assert.Equal("islenmis-veri", result.Outputs["final"]);
    }

    [Fact]
    public async Task ActivityProperty_UsesDeclaredVariableDefault_WithMustacheSyntax()
    {
        var json = """
        {
          "schemaVersion": "1.0",
          "id": "f1f1f1f1-f1f1-f1f1-f1f1-f1f1f1f1f1f1",
          "name": "Degiskenli Aktivite",
          "version": "1.0.0",
          "variables": [ { "name": "siteUrl", "type": "string", "default": "example.com" } ],
          "arguments": { "out": [ { "name": "captured", "type": "string" } ] },
          "nodes": [
            { "id": "n1", "type": "activity", "activity": "Test.CaptureUrl",
              "properties": { "url": "{{siteUrl}}" } }
          ],
          "connections": []
        }
        """;

        var factory = new MapFactory().Add(
            "Test.CaptureUrl",
            () => new FakeActivity("Test.CaptureUrl", ctx =>
            {
                var url = ctx.GetVariable<string>("url");
                return Task.FromResult(new Dictionary<string, object?> { ["captured"] = url });
            }));

        var runner = CreateRunner(
            catalog: new Dictionary<string, ActivityMetadata> { ["Test.CaptureUrl"] = Meta("Test.CaptureUrl") },
            factory: factory);

        var result = await runner.ExecuteAsync(Version(json), new(), Guid.NewGuid());

        Assert.True(result.Success, result.Exception?.Message);
        Assert.Equal("example.com", result.Outputs["captured"]);
    }

    // ---------------------------------------------------------------- birim testler

    [Fact]
    public void VariableScope_PushPop_IsolatesInnerVariables()
    {
        var scope = new VariableScope();
        scope.SetGlobalVariable("g", "global");

        scope.PushScope("inner");
        scope.SetLocalVariable("local", "içeride");
        Assert.Equal("içeride", scope.GetVariable<string>("local"));
        Assert.Equal("global", scope.GetVariable<string>("g")); // dış scope okunabilir

        scope.PopScope();
        Assert.False(scope.TryGetVariable("local", out _)); // izole → dışa sızmaz
        Assert.Equal("global", scope.GetVariable<string>("g"));
    }

    [Fact]
    public void VariableScope_MissingVariable_ReturnsDefault()
    {
        var scope = new VariableScope();
        Assert.Null(scope.GetVariable<string>("yok"));
        Assert.Equal(0, scope.GetVariable<int>("yok"));
    }

    [Fact]
    public void ExpressionEvaluator_ResolvesNestedDictionaryFieldInsideJsonPayload()
    {
        // Desktop.SendKeys senaryosu: 'keys' yapısal JSON'dur ve içinde {{fatura.FaturaNo}} geçer.
        // E-Fatura çıktısı OrdinalIgnoreCase bir Dictionary'dir.
        var invoice = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["FaturaNo"] = "NSE2026000000332",
            ["BosAlan"] = null,
        };
        var scope = new VariableScope();
        scope.SetGlobalVariable("fatura", invoice);
        var eval = new ExpressionEvaluator(scope);

        var keys = """[{"type":"text","text":"{{fatura.FaturaNo}}","waitMs":0}]""";
        Assert.Equal(
            """[{"type":"text","text":"NSE2026000000332","waitMs":0}]""",
            eval.EvaluateString(keys));

        // Farklı harf düzeni de çözülür (sözlük OrdinalIgnoreCase).
        Assert.Equal("NSE2026000000332", eval.EvaluateString("{{fatura.faturano}}"));

        // Alan VAR ama değeri null ise sonuç boş string olur — kullanıcının gördüğü belirti budur.
        Assert.Equal("", eval.EvaluateString("{{fatura.BosAlan}}"));
        // Alan hiç yoksa da boş string.
        Assert.Equal("", eval.EvaluateString("{{fatura.YokAlan}}"));
    }

    [Fact]
    public void ExpressionEvaluator_Comparison_And_Template()
    {
        var scope = new VariableScope();
        scope.SetGlobalVariable("a", 10L);
        scope.SetGlobalVariable("b", 5L);
        scope.SetGlobalVariable("name", "Ada");
        var eval = new ExpressionEvaluator(scope);

        Assert.True(eval.EvaluateCondition("${a} > ${b}"));
        Assert.False(eval.EvaluateCondition("${a} == ${b}"));
        Assert.True(eval.EvaluateCondition("${a} == 10"));
        Assert.Equal("Merhaba Ada", eval.EvaluateString("Merhaba ${name}"));
        Assert.Equal(10L, eval.EvaluateValue("${a}"));
        Assert.True(eval.EvaluateCondition("{{a}} > {{b}}"));
        Assert.Equal("Merhaba Ada", eval.EvaluateString("Merhaba {{name}}"));
        Assert.Equal(10L, eval.EvaluateValue("{{a}}"));
    }

    [Theory]
    [InlineData("${items}")]
    [InlineData("{{items}}")]
    public void ExpressionEvaluator_SingleVariableSyntaxPreservesEnumerableType(string expression)
    {
        var items = new List<long> { 1, 2 };
        var scope = new VariableScope();
        scope.SetGlobalVariable("items", items);

        var result = new ExpressionEvaluator(scope).EvaluateValue(expression);

        Assert.Same(items, result);
    }

    [Fact]
    public void ExpressionEvaluator_TypedDtoPathIsCaseInsensitiveAndMissingPropertyReturnsNull()
    {
        var scope = new VariableScope();
        scope.SetGlobalVariable("line", new InvoiceLineData { ItemCode = "URUN-1" });
        var evaluator = new ExpressionEvaluator(scope);

        Assert.Equal("URUN-1", evaluator.EvaluateValue("{{line.itemcode}}"));
        Assert.Null(evaluator.EvaluateValue("{{line.missing}}"));
    }

    [Fact]
    public void ExpressionEvaluator_DoesNotLeakRawReflectionErrorsForUnsafeProperties()
    {
        var scope = new VariableScope();
        scope.SetGlobalVariable("indexed", new IndexedValue());
        scope.SetGlobalVariable("throwing", new ThrowingValue());
        var evaluator = new ExpressionEvaluator(scope);

        Assert.Null(evaluator.EvaluateValue("{{indexed.item}}"));
        Assert.Throws<BusinessException>(() => evaluator.EvaluateValue("{{throwing.value}}"));
    }

    [Fact]
    public void ExpressionEvaluator_MixedOperators_RespectsPrecedence()
    {
        var scope = new VariableScope();
        scope.SetGlobalVariable("a", 1L);
        scope.SetGlobalVariable("b", 1L);
        var eval = new ExpressionEvaluator(scope);

        // Basic comparison: 1 > 0 should be true
        Assert.True(eval.EvaluateCondition("1 > 0"));

        // Variable comparison: ${b} > 0 should be true
        Assert.True(eval.EvaluateCondition("${b} > 0"));

        // Nested: ${a} == 1 > 0 means ${a} == (1 > 0)
        // Right operand "1 > 0" should be evaluated as condition (true),
        // then compared: 1 == true (converts true to 1) → true
        Assert.True(eval.EvaluateCondition("${a} == 1 > 0"));
    }

    [Fact]
    public async Task ComponentCall_WithUnresolvedId_ErrorMessagePreservesId()
    {
        var json = """
        {
          "schemaVersion": "1.0",
          "id": "ffffffff-ffff-ffff-ffff-ffffffffffff",
          "name": "TestUnresolved",
          "version": "1.0.0",
          "nodes": [
            { "id": "cc", "type": "componentCall", "componentId": "missing-component-id",
              "componentVersion": "1.0.0",
              "inputMapping": {},
              "outputMapping": {} }
          ],
          "connections": []
        }
        """;

        var runner = CreateRunner(componentResolver: (id, ver) => null);
        var result = await runner.ExecuteAsync(Version(json), new(), Guid.NewGuid());

        Assert.False(result.Success);
        Assert.IsType<SystemException>(result.Exception);
        // Error message should show the actual ComponentId
        Assert.Contains("missing-component-id", result.Exception!.Message);
    }

    [Fact]
    public async Task ResumeWithCheckpoint_EmptyOverrides_RestoresState()
    {
        var componentJson = """
        {
          "schemaVersion": "1.0",
          "id": "12121212-1212-1212-1212-121212121212",
          "name": "Stateful",
          "version": "1.0.0",
          "variables": [ { "name": "counter", "type": "int", "default": 0 },
                         { "name": "next", "type": "int", "default": 0 } ],
          "arguments": { "out": [ { "name": "next", "type": "int" } ] },
          "nodes": [
            { "id": "cp", "type": "checkpoint" },
            { "id": "assign_next", "type": "assign", "variableName": "next", "value": "${counter}" }
          ],
          "connections": [ { "from": "cp", "to": "assign_next" } ]
        }
        """;

        var runner = CreateRunner();

        // First execution: reach checkpoint, set counter=5
        var result1 = await runner.ExecuteAsync(Version(componentJson), new() { ["counter"] = 5 }, Guid.NewGuid());
        Assert.True(result1.Success);
        Assert.NotNull(result1.CheckpointData);
        Assert.Equal(5L, Convert.ToInt64(result1.Outputs["next"]));

        // Resume with empty override dict: should restore checkpoint state (counter=5) then continue
        var result2 = await runner.ResumeAsync(
            new WorkflowVersion { JsonDefinition = componentJson },
            new(), // Empty override dict!
            result1.CheckpointData!,
            Guid.NewGuid());

        Assert.True(result2.Success);
        // If checkpoint state was properly imported despite empty dict, counter should still be 5
        Assert.Equal(5L, Convert.ToInt64(result2.Outputs["next"]));
    }

    private sealed class IndexedValue
    {
        public string this[int index] => index.ToString();
    }

    private sealed class ThrowingValue
    {
        public string Value => throw new InvalidOperationException("sensitive getter detail");
    }

    private sealed class RecordingObserver : IWorkflowExecutionObserver
    {
        public List<NodeExecutionEvent> Completed { get; } = [];
        public void OnNodeStarted(NodeExecutionEvent evt) { }
        public void OnNodeCompleted(NodeExecutionEvent evt) => Completed.Add(evt);
    }
}

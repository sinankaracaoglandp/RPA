namespace RPA.Infrastructure.Tests.Components;

using Microsoft.Extensions.Logging.Abstractions;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Services;
using RPA.Infrastructure.Workflow;

/// <summary>
/// Task 4.5 — "SAP Login" component'i: JSON tanımı geçerliliği, uçtan uca çalıştırma
/// (mocked SAP GUI + OTP kanalları) ve publish/approve governance akışı (Spec Bölüm 9).
/// Bu, publish/approve modelinin ilk uçtan uca doğrulamasıdır.
/// </summary>
public class SapLoginComponentTests
{
    private static readonly Guid SapLoginComponentId =
        Guid.Parse("00000000-0000-0000-0000-000000000101");

    private static string LoadComponentJson()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "RPA.Infrastructure", "Components", "SapLoginComponent.json");
        path = Path.GetFullPath(path);
        return File.ReadAllText(path);
    }

    // Basit stub aktivite — sabit çıkış döner (mocked SAP GUI / OTP kanalları).
    private sealed class StubActivity : IActivity
    {
        private readonly string _activityId;
        private readonly Dictionary<string, object?> _outputs;

        public StubActivity(string activityId, Dictionary<string, object?> outputs)
        {
            _activityId = activityId;
            _outputs = outputs;
        }

        public ActivityMetadata GetMetadata() => new()
        {
            ActivityId = _activityId,
            DisplayName = _activityId,
            Category = "Test",
            Description = "stub",
            Inputs = new(),
            Outputs = new(),
        };

        public Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext context)
            => Task.FromResult(new Dictionary<string, object?>(_outputs));
    }

    private static IActivityFactory MockedSapAndOtpFactory() => new DelegateActivityFactory(id => id switch
    {
        "Sap.Gui.Connect" => new StubActivity("Sap.Gui.Connect", new()),
        "Otp.GetOtp" => new StubActivity("Otp.GetOtp", new()
        {
            ["otpCode"] = "123456",
            ["channelUsed"] = "Totp",
            ["otpRequestId"] = "otp-req-1",
        }),
        "Sap.Gui.GetText" => new StubActivity("Sap.Gui.GetText", new()
        {
            ["text"] = "Giriş başarılı",
        }),
        _ => null,
    });

    private static ComponentRunner CreateComponentRunner(IComponentStore store)
    {
        var resolver = new ComponentResolver(store);
        var baseRunner = new BaseRunner(
            new WorkflowValidator(),
            new ActivityCatalog(),
            MockedSapAndOtpFactory(),
            NullLogger<BaseRunner>.Instance,
            vault: null,
            componentResolver: null);
        return new ComponentRunner(resolver, baseRunner);
    }

    // ---------------------------------------------------------------- JSON tanımı

    [Fact]
    public void ComponentJson_IsValidWorkflowDefinition()
    {
        var json = LoadComponentJson();
        var validator = new WorkflowValidator();

        var result = validator.ValidateWorkflowJson(json);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    [Fact]
    public void ComponentJson_DeclaresExpectedInputsAndOutputs()
    {
        var json = LoadComponentJson();

        Assert.Contains("\"systemName\"", json);
        Assert.Contains("\"client\"", json);
        Assert.Contains("\"credentialRef\"", json);
        Assert.Contains("\"sessionId\"", json);
        Assert.Contains("\"loggedInUser\"", json);
        Assert.Contains("Sap.Gui.Connect", json);
        Assert.Contains("Otp.GetOtp", json);
        Assert.Contains("Sap.Gui.GetText", json);
    }

    // ---------------------------------------------------------------- Uçtan uca çalıştırma

    [Fact]
    public async Task ComponentJson_ExecutesEndToEnd_WithMockedSapAndOtp()
    {
        var json = LoadComponentJson();
        var store = new InMemoryComponentStore().Add(new ComponentVersion
        {
            Id = Guid.NewGuid(),
            ComponentId = SapLoginComponentId,
            Version = "1.0.0",
            Status = ComponentStatus.Published,
            JsonDefinition = json,
        });

        var runner = CreateComponentRunner(store);

        var outputs = await runner.InvokeAsync(
            SapLoginComponentId.ToString(),
            "1.0.0",
            new Dictionary<string, object?>
            {
                ["systemName"] = "DEV",
                ["client"] = "100",
                ["credentialRef"] = "sap-dev-user",
            },
            Guid.NewGuid());

        Assert.Equal("otp-req-1", outputs["sessionId"]);
        Assert.Equal("sap-dev-user", outputs["loggedInUser"]);
    }

    // ---------------------------------------------------------------- Publish/Approve governance

    [Fact]
    public async Task PublishAsync_RequiresDeveloperRole()
    {
        var service = new ComponentPublishService(new InMemoryComponentPublishRepository());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.PublishAsync(
            SapLoginComponentId, "1.0.0", LoadComponentJson(), "{}",
            callerRoles: new[] { "Viewer" }));
    }

    [Fact]
    public async Task PublishAsync_CreatesDraftVersion_WhenDeveloperRole()
    {
        var service = new ComponentPublishService(new InMemoryComponentPublishRepository());

        var result = await service.PublishAsync(
            SapLoginComponentId, "1.0.0", LoadComponentJson(), "{}",
            callerRoles: new[] { "Developer" });

        Assert.Equal(ComponentStatus.Draft, result.Status);
        Assert.Equal("1.0.0", result.Version);
        Assert.Equal(SapLoginComponentId, result.ComponentId);
    }

    [Fact]
    public async Task PublishAsync_PersistsLibraryMetadata_WhenProvided()
    {
        var repo = new InMemoryComponentPublishRepository();
        var service = new ComponentPublishService(repo);

        await service.PublishAsync(
            SapLoginComponentId, "1.0.0", LoadComponentJson(), "{}", new[] { "Developer" },
            displayName: "SAP Login", description: "OTP'li SAP oturumu",
            author: "sinan", category: "SAP");

        // Kütüphane metadata'sı KAYITTA kalıcı olmalı (yalnızca dönen nesnede değil).
        var persisted = await repo.FindAsync(SapLoginComponentId, "1.0.0");
        Assert.NotNull(persisted);
        Assert.Equal("SAP Login", persisted!.DisplayName);
        Assert.Equal("OTP'li SAP oturumu", persisted.Description);
        Assert.Equal("sinan", persisted.Author);
        Assert.Equal("SAP", persisted.Category);
    }

    [Fact]
    public async Task ApproveAsync_RequiresApproverRole()
    {
        var repo = new InMemoryComponentPublishRepository();
        var service = new ComponentPublishService(repo);
        await service.PublishAsync(
            SapLoginComponentId, "1.0.0", LoadComponentJson(), "{}", new[] { "Developer" });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApproveAsync(
            SapLoginComponentId, "1.0.0", callerRoles: new[] { "Developer" }));
    }

    [Fact]
    public async Task ApproveAsync_MarksPublished_AfterDraft()
    {
        var repo = new InMemoryComponentPublishRepository();
        var service = new ComponentPublishService(repo);
        await service.PublishAsync(
            SapLoginComponentId, "1.0.0", LoadComponentJson(), "{}", new[] { "Developer" });

        var approved = await service.ApproveAsync(
            SapLoginComponentId, "1.0.0", callerRoles: new[] { "Approver" });

        Assert.Equal(ComponentStatus.Published, approved.Status);
    }

    [Fact]
    public async Task Resolver_OnlySeesPublishedVersion_NotDraft()
    {
        var repo = new InMemoryComponentPublishRepository();
        var service = new ComponentPublishService(repo);
        await service.PublishAsync(
            SapLoginComponentId, "1.0.0", LoadComponentJson(), "{}", new[] { "Developer" });

        // Henüz onaylanmadı — resolver Published olmayan versiyonu göremez.
        var resolver = new ComponentResolver(repo);
        Assert.Null(resolver.Resolve(SapLoginComponentId.ToString(), null));

        await service.ApproveAsync(SapLoginComponentId, "1.0.0", new[] { "Approver" });

        var resolved = resolver.Resolve(SapLoginComponentId.ToString(), null);
        Assert.NotNull(resolved);
        Assert.Equal(ComponentStatus.Published, resolved!.Status);
    }
}

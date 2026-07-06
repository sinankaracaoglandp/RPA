namespace RPA.Infrastructure.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Retry;
using RPA.Infrastructure.Workflow;
using BusinessException = RPA.Domain.Exceptions.BusinessException;

/// <summary>
/// WP-6.5 — Pilot senaryosu doğrulaması (Spec Bölüm 15, Doğrulama Checklist).
/// Senaryo: "Müşteri portalından (OTP'li giriş) veri çekip SAP MM01'de malzeme açma".
/// 100 kayıtlık batch, hedef ≥%95 straight-through başarı. BusinessException'lar
/// (malzeme zaten mevcut) Action Center'a düşer; geçici SystemException'lar (portal
/// timeout) retry ile toparlanır ve başarıya sayılır.
///
/// Gerçek üretim bileşenleri kullanılır: BaseRunner (state machine), RetryHandler +
/// ExceptionClassifier (retry/sınıflandırma). Portal/OTP/SAP kanalları deterministik
/// sahtelerle temsil edilir — bu bir pilot koşum simülasyonudur, canlı bağımlılık değil.
/// </summary>
public class PilotScenarioTests
{
    private const int BatchSize = 100;

    // Pilot workflow JSON (pilot/mm01-material-creation.workflow.json ile hizalı).
    private static readonly string PilotWorkflowJson = """
    {
      "schemaVersion": "1.0",
      "id": "6a5f0000-0000-0000-0000-000000000001",
      "name": "Pilot MM01",
      "version": "1.0.0",
      "arguments": {
        "in": [ { "name": "recordId", "type": "int" },
                { "name": "materialName", "type": "string" } ],
        "out": [ { "name": "materialNumber", "type": "string" },
                 { "name": "result", "type": "string" } ]
      },
      "nodes": [
        { "id": "login", "type": "activity", "activity": "Web.Portal.Login",
          "properties": { "recordId": "${recordId}" } },
        { "id": "fetch", "type": "activity", "activity": "Web.Portal.FetchMaterial",
          "properties": { "recordId": "${recordId}", "materialName": "${materialName}" } },
        { "id": "create", "type": "activity", "activity": "Sap.Nco.CreateMaterial",
          "properties": { "recordId": "${recordId}", "materialName": "${materialName}" } },
        { "id": "done", "type": "assign", "variableName": "result",
          "value": "Malzeme ${materialNumber} olusturuldu" }
      ],
      "connections": [
        { "from": "login", "to": "fetch" },
        { "from": "fetch", "to": "create" },
        { "from": "create", "to": "done" }
      ]
    }
    """;

    /// <summary>Kayıt başına geçici hata sayacı — retry davranışını modellemek için.</summary>
    private sealed class FakeActivity : IActivity
    {
        private readonly Func<IActivityExecutionContext, Task<Dictionary<string, object?>>> _fn;
        private readonly ActivityMetadata _meta;
        public FakeActivity(string id, Func<IActivityExecutionContext, Task<Dictionary<string, object?>>> fn)
        {
            _meta = new ActivityMetadata { ActivityId = id, DisplayName = id };
            _fn = fn;
        }
        public Task<Dictionary<string, object?>> ExecuteAsync(IActivityExecutionContext c) => _fn(c);
        public ActivityMetadata GetMetadata() => _meta;
    }

    private sealed class MapFactory : IActivityFactory
    {
        private readonly Dictionary<string, Func<IActivity>> _map = new();
        public MapFactory Add(string id, Func<IActivity> f) { _map[id] = f; return this; }
        public IActivity? CreateActivity(string activityId) =>
            _map.TryGetValue(activityId, out var f) ? f() : null;
    }

    private static (BaseRunner runner, ActivityCatalog catalog) BuildPilot(
        HashSet<int> transientOnce)
    {
        // Kayıt başına login denemesi sayacı (geçici hatayı yalnızca ilk denemede fırlat).
        var loginAttempts = new Dictionary<int, int>();

        var factory = new MapFactory()
            .Add("Web.Portal.Login", () => new FakeActivity("Web.Portal.Login", ctx =>
            {
                // OTP'li portal girişi. transientOnce kümesindeki kayıtlar ilk denemede
                // timeout (SystemException) atar; retry'da başarılı olur.
                var id = ctx.GetVariable<int>("recordId");
                var n = loginAttempts.TryGetValue(id, out var v) ? v : 0;
                loginAttempts[id] = n + 1;
                if (transientOnce.Contains(id) && n == 0)
                {
                    throw new SystemException($"Portal OTP timeout (kayıt {id}) — geçici.");
                }
                return Task.FromResult(new Dictionary<string, object?> { ["session"] = $"s-{id}" });
            }))
            .Add("Web.Portal.FetchMaterial", () => new FakeActivity("Web.Portal.FetchMaterial", ctx =>
            {
                var name = ctx.GetVariable<string>("materialName");
                return Task.FromResult(new Dictionary<string, object?> { ["fetchedName"] = name });
            }))
            .Add("Sap.Nco.CreateMaterial", () => new FakeActivity("Sap.Nco.CreateMaterial", ctx =>
            {
                // SAP MM01 BAPI. recordId % 33 == 0 → malzeme zaten mevcut (BusinessException).
                var id = ctx.GetVariable<int>("recordId");
                if (id % 33 == 0)
                {
                    throw new BusinessException($"Malzeme zaten mevcut (kayıt {id}).");
                }
                return Task.FromResult(new Dictionary<string, object?>
                {
                    ["materialNumber"] = $"MAT-{id:D6}",
                });
            }));

        var catalog = new ActivityCatalog(new Dictionary<string, ActivityMetadata>
        {
            ["Web.Portal.Login"] = new() { ActivityId = "Web.Portal.Login", DisplayName = "Portal Giriş" },
            ["Web.Portal.FetchMaterial"] = new() { ActivityId = "Web.Portal.FetchMaterial", DisplayName = "Malzeme Çek" },
            ["Sap.Nco.CreateMaterial"] = new() { ActivityId = "Sap.Nco.CreateMaterial", DisplayName = "MM01 Oluştur" },
        });

        var runner = new BaseRunner(
            new WorkflowValidator(), catalog, factory, NullLogger<BaseRunner>.Instance);
        return (runner, catalog);
    }

    private sealed record BatchResult(
        int Success, int BusinessExceptions, int HardFailures, List<string> ActionCenter)
    {
        public double SuccessRate => Success * 100.0 / (Success + BusinessExceptions + HardFailures);
    }

    /// <summary>
    /// Kuyruk motoru semantiğini yansıtan batch orkestratörü: her kayıt için workflow'u
    /// çalıştırır; System hatasında retry (MaxRetries+1 deneme), Business hatasında
    /// Action Center'a yönlendirir, tükenirse hard failure sayar.
    /// </summary>
    private static async Task<BatchResult> RunBatchAsync(int batchSize, HashSet<int> transientOnce)
    {
        var (runner, _) = BuildPilot(transientOnce);
        var classifier = new ExceptionClassifier();
        var policy = new RetryPolicy(maxAttempts: 3, initialDelay: TimeSpan.Zero);

        var success = 0;
        var business = 0;
        var hard = 0;
        var actionCenter = new List<string>();

        var version = new RPA.Domain.Entities.WorkflowVersion { JsonDefinition = PilotWorkflowJson };

        for (var id = 1; id <= batchSize; id++)
        {
            var attempt = 0;
            while (true)
            {
                attempt++;
                var args = new Dictionary<string, object?>
                {
                    ["recordId"] = id,
                    ["materialName"] = $"Malzeme-{id}",
                };
                var result = await runner.ExecuteAsync(version, args, Guid.NewGuid());

                if (result.Success)
                {
                    success++;
                    break;
                }

                var ex = result.Exception ?? new SystemException("bilinmeyen");
                if (classifier.Classify(ex) == ExceptionType.Business)
                {
                    business++;
                    actionCenter.Add(ex.Message); // BusinessException → Action Center kaydı
                    break;
                }

                // System hatası — retry politikası
                if (policy.ShouldRetry(attempt))
                {
                    continue;
                }
                hard++;
                break;
            }
        }

        return new BatchResult(success, business, hard, actionCenter);
    }

    [Fact]
    public async Task Pilot_100RecordBatch_MeetsSuccessTarget()
    {
        // 25,50,75,100 kayıtları portal girişinde geçici timeout yaşar (retry ile toparlanır).
        var transient = new HashSet<int> { 25, 50, 75, 100 };

        var result = await RunBatchAsync(BatchSize, transient);

        // Hedef: ≥%95 straight-through başarı (Spec Bölüm 15 pilot kriteri).
        Assert.True(result.SuccessRate >= 95.0,
            $"Başarı oranı %{result.SuccessRate:F1} — hedef %95. " +
            $"(Başarılı {result.Success}, İş istisnası {result.BusinessExceptions}, Hard {result.HardFailures})");

        // Hiç hard failure olmamalı — geçici hatalar retry ile toparlandı.
        Assert.Equal(0, result.HardFailures);
    }

    [Fact]
    public async Task Pilot_BusinessExceptions_RoutedToActionCenter()
    {
        var result = await RunBatchAsync(BatchSize, new HashSet<int>());

        // 33, 66, 99 → malzeme zaten mevcut = 3 BusinessException.
        Assert.Equal(3, result.BusinessExceptions);
        Assert.Equal(3, result.ActionCenter.Count);
        Assert.All(result.ActionCenter, m => Assert.Contains("zaten mevcut", m));
    }

    [Fact]
    public async Task Pilot_TransientSystemErrors_RecoveredByRetry()
    {
        // Yalnızca bir kayıt geçici hata yaşasın; başarıya sayılmalı, hard failure olmamalı.
        var result = await RunBatchAsync(10, new HashSet<int> { 3 });

        Assert.Equal(0, result.HardFailures);
        // 10 kayıtta business exception yok (33'ün katı yok) → 10 başarı.
        Assert.Equal(10, result.Success);
    }
}

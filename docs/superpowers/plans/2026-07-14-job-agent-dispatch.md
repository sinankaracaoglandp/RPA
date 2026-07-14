# Job → Ajan (Tag Havuzu) Dispatch + Zamanlama Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Studio'da oluşturulan workflow'ların hangi ajanda (Robot) koşacağını `Trigger` katmanında tag havuzu ile tanımlamak, tetiklenince uygun ajanı seçip `JobRun.AssignedRobotId`'yi doldurmak ve job'ları yöneten bir Studio ekranı eklemek.

**Architecture:** Onion mimarisi. `Trigger` entity'sine ajan hedefleme alanları eklenir; yeni `IRobotDispatcher` (Application/Domain) online + kapasitesi uygun + tag'leri kapsayan bir robot seçer; `TriggerService` bunu `NewJobRun`'a bağlar; API yeni `GET /api/triggers` sunar; Studio `orchestrator/schedules` ekranı job tanımlarını yönetir. Agent'a gerçek teslim (poll/handoff) KAPSAM DIŞI.

**Tech Stack:** C# / .NET 10, EF Core (Npgsql), xUnit, Angular (standalone components, signals), Vitest.

## Global Constraints

- Onion katman bağımlılığı: Domain harici bağımlılık YOK; Application → Domain; Infrastructure → Application+Domain; WebAPI → Application+Infrastructure.
- TDD zorunlu: failing test → minimal impl → pass → commit.
- Hedef mod yalnız **Unattended**. Ajan seçimi yalnız **tag havuzu** (sabit tek-ajan yok).
- Tag karşılaştırma: virgülle ayrık, trim + case-insensitive; job'un TÜM tag'leri robotta olmalı (kapsama).
- Database snake_case (EF otomatik). Migration adı: `AddTriggerRobotTargeting`.
- Kod & metin Türkçe yorum konvansiyonuna uyar (mevcut dosya deseni).
- Codex'in bölgesine (canvas/designer/döngü node'ları) DOKUNMA.
- Yeni JobRun status değeri: `"Pending"` (aday ajan yokken).

---

### Task 1: Domain — Trigger'a ajan hedefleme alanları + migration

**Files:**
- Modify: `src/RPA.Domain/Entities/Trigger.cs`
- Test: `tests/RPA.Domain.Tests/TriggerTests.cs` (create veya mevcut entity testine ek)
- Migration: `src/RPA.Infrastructure/Migrations/` (EF generate)

**Interfaces:**
- Produces: `Trigger.TargetRobotTags` (string, default ""), `Trigger.Priority` (int, default 0).

- [ ] **Step 1: Write the failing test**

`tests/RPA.Domain.Tests/TriggerTests.cs`:
```csharp
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using Xunit;

namespace RPA.Domain.Tests;

public class TriggerTests
{
    [Fact]
    public void Trigger_HasRobotTargetingDefaults()
    {
        var trigger = new Trigger();
        Assert.Equal("", trigger.TargetRobotTags);
        Assert.Equal(0, trigger.Priority);
    }

    [Fact]
    public void Trigger_CanSetRobotTargeting()
    {
        var trigger = new Trigger { TargetRobotTags = "prod-vm,sap", Priority = 5, Type = TriggerType.Cron };
        Assert.Equal("prod-vm,sap", trigger.TargetRobotTags);
        Assert.Equal(5, trigger.Priority);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/RPA.Domain.Tests -v minimal`
Expected: FAIL — `Trigger` `TargetRobotTags`/`Priority` üyelerini içermiyor (derleme hatası).

- [ ] **Step 3: Write minimal implementation**

`src/RPA.Domain/Entities/Trigger.cs` — mevcut alanlara ekle:
```csharp
using RPA.Domain.Enums;

namespace RPA.Domain.Entities;

public class Trigger : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }
    public Guid WorkflowVersionId { get; set; }
    public TriggerType Type { get; set; }
    public string Configuration { get; set; } = "{}"; // JSON: cron, webhook URL, etc.
    public Guid EnvironmentId { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Bu job'un hangi robot havuzunda koşacağı — virgülle ayrık etiketler
    /// (örn. "prod-vm,sap"). Boşsa etiket kısıtı yok. Robot.Tags bunları kapsamalı.</summary>
    public string TargetRobotTags { get; set; } = "";

    /// <summary>Eşit uygunlukta adaylar arasında sıralama önceliği (büyük = önce).</summary>
    public int Priority { get; set; } = 0;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/RPA.Domain.Tests -v minimal`
Expected: PASS.

- [ ] **Step 5: Generate migration**

Run:
```bash
dotnet ef migrations add AddTriggerRobotTargeting --project src/RPA.Infrastructure --startup-project src/RPA.WebAPI
```
Expected: yeni migration dosyası `triggers` tablosuna `target_robot_tags` (text, default '') ve `priority` (int, default 0) kolonlarını ekler. Migration'ı aç, `AddColumn` çağrılarının bu iki kolonu içerdiğini gözle doğrula.

- [ ] **Step 6: Build to verify migration compiles**

Run: `dotnet build src/RPA.Infrastructure`
Expected: SUCCESS.

- [ ] **Step 7: Commit**

```bash
git add src/RPA.Domain/Entities/Trigger.cs tests/RPA.Domain.Tests/TriggerTests.cs src/RPA.Infrastructure/Migrations/
git commit -m "feat(domain): Trigger'a ajan hedefleme (TargetRobotTags, Priority) + migration"
```

---

### Task 2: Domain/Infrastructure — Tüm trigger'ları listeleme + aktif iş sayacı

**Files:**
- Modify: `src/RPA.Domain/Interfaces/ITriggerRepository.cs`
- Modify: `src/RPA.Infrastructure/Persistence/EfTriggerRepository.cs`
- Test: `tests/RPA.Infrastructure.Tests/EfTriggerRepositoryTests.cs` (create)

**Interfaces:**
- Consumes: `Trigger` (Task 1).
- Produces:
  - `ITriggerRepository.ListTriggersAsync(Guid? projectId, Guid? environmentId, bool? isActive, CancellationToken) → Task<IReadOnlyList<Trigger>>`
  - `ITriggerRepository.GetActiveJobCountsByRobotAsync(CancellationToken) → Task<IReadOnlyDictionary<Guid,int>>` (Status=="Running" JobRun'ların AssignedRobotId'ye göre sayımı)

- [ ] **Step 1: Write the failing test**

`tests/RPA.Infrastructure.Tests/EfTriggerRepositoryTests.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Infrastructure.Persistence;
using Xunit;

namespace RPA.Infrastructure.Tests;

public class EfTriggerRepositoryTests
{
    private static RpaDbContext NewDb() =>
        new(new DbContextOptionsBuilder<RpaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task ListTriggersAsync_FiltersByProjectAndActive()
    {
        using var db = NewDb();
        var projectId = Guid.NewGuid();
        db.Triggers.Add(new Trigger { ProjectId = projectId, IsActive = true, Type = TriggerType.Cron });
        db.Triggers.Add(new Trigger { ProjectId = projectId, IsActive = false, Type = TriggerType.Manual });
        db.Triggers.Add(new Trigger { ProjectId = Guid.NewGuid(), IsActive = true, Type = TriggerType.Cron });
        await db.SaveChangesAsync();
        var repo = new EfTriggerRepository(db);

        var result = await repo.ListTriggersAsync(projectId, null, isActive: true, default);

        Assert.Single(result);
        Assert.Equal(projectId, result[0].ProjectId);
    }

    [Fact]
    public async Task GetActiveJobCountsByRobotAsync_CountsRunningPerRobot()
    {
        using var db = NewDb();
        var robotA = Guid.NewGuid();
        db.JobRuns.Add(new JobRun { AssignedRobotId = robotA, Status = "Running", StartedAt = DateTime.UtcNow });
        db.JobRuns.Add(new JobRun { AssignedRobotId = robotA, Status = "Running", StartedAt = DateTime.UtcNow });
        db.JobRuns.Add(new JobRun { AssignedRobotId = robotA, Status = "Successful", StartedAt = DateTime.UtcNow });
        db.JobRuns.Add(new JobRun { AssignedRobotId = null, Status = "Running", StartedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var repo = new EfTriggerRepository(db);

        var counts = await repo.GetActiveJobCountsByRobotAsync(default);

        Assert.Equal(2, counts[robotA]);
        Assert.False(counts.ContainsKey(Guid.Empty));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/RPA.Infrastructure.Tests --filter EfTriggerRepositoryTests -v minimal`
Expected: FAIL — `ListTriggersAsync`/`GetActiveJobCountsByRobotAsync` tanımlı değil (derleme hatası).

- [ ] **Step 3: Add interface methods**

`src/RPA.Domain/Interfaces/ITriggerRepository.cs` — arayüze ekle (SaveChangesAsync'ten önce):
```csharp
    /// <summary>Tüm trigger'ları (job tanımları) opsiyonel filtrelerle döner (Studio Zamanlamalar ekranı).</summary>
    Task<IReadOnlyList<Trigger>> ListTriggersAsync(
        Guid? projectId, Guid? environmentId, bool? isActive, CancellationToken cancellationToken = default);

    /// <summary>Status=="Running" JobRun'ları AssignedRobotId'ye göre sayar (kapasite kontrolü — dispatcher).</summary>
    Task<IReadOnlyDictionary<Guid, int>> GetActiveJobCountsByRobotAsync(CancellationToken cancellationToken = default);
```

- [ ] **Step 4: Implement in EF repository**

`src/RPA.Infrastructure/Persistence/EfTriggerRepository.cs` — SaveChangesAsync'ten önce ekle:
```csharp
    public async Task<IReadOnlyList<Trigger>> ListTriggersAsync(
        Guid? projectId, Guid? environmentId, bool? isActive, CancellationToken cancellationToken = default)
    {
        var query = _db.Triggers.Where(t => !t.IsDeleted);
        if (projectId.HasValue) query = query.Where(t => t.ProjectId == projectId.Value);
        if (environmentId.HasValue) query = query.Where(t => t.EnvironmentId == environmentId.Value);
        if (isActive.HasValue) query = query.Where(t => t.IsActive == isActive.Value);
        return await query.OrderByDescending(t => t.CreatedAt).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetActiveJobCountsByRobotAsync(CancellationToken cancellationToken = default)
    {
        var grouped = await _db.JobRuns
            .Where(j => j.Status == "Running" && j.AssignedRobotId != null)
            .GroupBy(j => j.AssignedRobotId!.Value)
            .Select(g => new { RobotId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        return grouped.ToDictionary(x => x.RobotId, x => x.Count);
    }
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/RPA.Infrastructure.Tests --filter EfTriggerRepositoryTests -v minimal`
Expected: PASS (2 test).

- [ ] **Step 6: Commit**

```bash
git add src/RPA.Domain/Interfaces/ITriggerRepository.cs src/RPA.Infrastructure/Persistence/EfTriggerRepository.cs tests/RPA.Infrastructure.Tests/EfTriggerRepositoryTests.cs
git commit -m "feat(infra): trigger listeleme filtreleri + robot bazli aktif is sayaci"
```

---

### Task 3: Domain — IRobotDispatcher ajan seçim algoritması

**Files:**
- Create: `src/RPA.Domain/Interfaces/IRobotDispatcher.cs`
- Create: `src/RPA.Infrastructure/Scheduling/RobotDispatcher.cs`
- Test: `tests/RPA.Infrastructure.Tests/RobotDispatcherTests.cs`

**Interfaces:**
- Consumes: `IRobotService.ListAsync` (robotlar), `ITriggerRepository.GetActiveJobCountsByRobotAsync` (Task 2), `Trigger.TargetRobotTags`/`Priority` (Task 1), `Robot` (MachineName, Tags, Status, LastHeartbeat, Capacity), `RobotStatus.Online`.
- Produces: `IRobotDispatcher.SelectRobotAsync(Trigger trigger, CancellationToken) → Task<Robot?>` (null = uygun aday yok).

- [ ] **Step 1: Write the failing test**

`tests/RPA.Infrastructure.Tests/RobotDispatcherTests.cs`:
```csharp
using Moq;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Scheduling;
using Xunit;

namespace RPA.Infrastructure.Tests;

public class RobotDispatcherTests
{
    private static Robot Rbt(string name, string tags, RobotStatus status, int cap, DateTime hb) =>
        new() { Id = Guid.NewGuid(), MachineName = name, Tags = tags, Status = status, Capacity = cap, LastHeartbeat = hb };

    private static IRobotDispatcher Build(IEnumerable<Robot> robots, IReadOnlyDictionary<Guid, int> active)
    {
        var robotSvc = new Mock<IRobotService>();
        robotSvc.Setup(s => s.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(robots.ToList());
        var repo = new Mock<ITriggerRepository>();
        repo.Setup(r => r.GetActiveJobCountsByRobotAsync(It.IsAny<CancellationToken>())).ReturnsAsync(active);
        return new RobotDispatcher(robotSvc.Object, repo.Object);
    }

    [Fact]
    public async Task SelectRobot_RequiresTagCoverage()
    {
        var ok = Rbt("A", "prod-vm,sap,extra", RobotStatus.Online, 1, DateTime.UtcNow);
        var missing = Rbt("B", "prod-vm", RobotStatus.Online, 1, DateTime.UtcNow);
        var d = Build(new[] { missing, ok }, new Dictionary<Guid, int>());

        var result = await d.SelectRobotAsync(new Trigger { TargetRobotTags = "prod-vm,sap" }, default);

        Assert.Equal(ok.Id, result!.Id);
    }

    [Fact]
    public async Task SelectRobot_SkipsOfflineAndFullCapacity()
    {
        var offline = Rbt("A", "x", RobotStatus.Offline, 5, DateTime.UtcNow);
        var full = Rbt("B", "x", RobotStatus.Online, 1, DateTime.UtcNow);
        var free = Rbt("C", "x", RobotStatus.Online, 2, DateTime.UtcNow);
        var d = Build(new[] { offline, full, free },
            new Dictionary<Guid, int> { [full.Id] = 1, [free.Id] = 1 });

        var result = await d.SelectRobotAsync(new Trigger { TargetRobotTags = "x" }, default);

        Assert.Equal(free.Id, result!.Id);
    }

    [Fact]
    public async Task SelectRobot_ReturnsNull_WhenNoCandidate()
    {
        var full = Rbt("B", "x", RobotStatus.Online, 1, DateTime.UtcNow);
        var d = Build(new[] { full }, new Dictionary<Guid, int> { [full.Id] = 1 });

        var result = await d.SelectRobotAsync(new Trigger { TargetRobotTags = "x" }, default);

        Assert.Null(result);
    }

    [Fact]
    public async Task SelectRobot_PrefersMostFreeCapacity()
    {
        var lessFree = Rbt("A", "x", RobotStatus.Online, 3, DateTime.UtcNow); // free 2
        var moreFree = Rbt("B", "x", RobotStatus.Online, 5, DateTime.UtcNow); // free 4
        var d = Build(new[] { lessFree, moreFree },
            new Dictionary<Guid, int> { [lessFree.Id] = 1, [moreFree.Id] = 1 });

        var result = await d.SelectRobotAsync(new Trigger { TargetRobotTags = "x" }, default);

        Assert.Equal(moreFree.Id, result!.Id);
    }

    [Fact]
    public async Task SelectRobot_EmptyTargetTags_MatchesAnyOnline()
    {
        var any = Rbt("A", "", RobotStatus.Online, 1, DateTime.UtcNow);
        var d = Build(new[] { any }, new Dictionary<Guid, int>());

        var result = await d.SelectRobotAsync(new Trigger { TargetRobotTags = "" }, default);

        Assert.Equal(any.Id, result!.Id);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/RPA.Infrastructure.Tests --filter RobotDispatcherTests -v minimal`
Expected: FAIL — `IRobotDispatcher`/`RobotDispatcher` yok (derleme hatası).

- [ ] **Step 3: Create the interface**

`src/RPA.Domain/Interfaces/IRobotDispatcher.cs`:
```csharp
namespace RPA.Domain.Interfaces;

using RPA.Domain.Entities;

/// <summary>
/// Bir tetikleyici (job) ateşlendiğinde onu çalıştıracak uygun Robot'u (Unattended ajan) seçer.
/// Aday = Online + kapasitesi müsait + Tags, Trigger.TargetRobotTags'i kapsayan robot.
/// Aday yoksa null (JobRun Pending kalır).
/// </summary>
public interface IRobotDispatcher
{
    Task<Robot?> SelectRobotAsync(Trigger trigger, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Implement the dispatcher**

`src/RPA.Infrastructure/Scheduling/RobotDispatcher.cs`:
```csharp
namespace RPA.Infrastructure.Scheduling;

using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;

/// <summary>
/// Tag havuzu tabanlı ajan seçici. Online + kapasitesi boş + Trigger tag'lerini kapsayan robotlar
/// arasından en boş kapasiteli → en yüksek Trigger.Priority → en eski heartbeat olanı seçer.
/// </summary>
public sealed class RobotDispatcher : IRobotDispatcher
{
    private readonly IRobotService _robotService;
    private readonly ITriggerRepository _triggerRepository;

    public RobotDispatcher(IRobotService robotService, ITriggerRepository triggerRepository)
    {
        _robotService = robotService ?? throw new ArgumentNullException(nameof(robotService));
        _triggerRepository = triggerRepository ?? throw new ArgumentNullException(nameof(triggerRepository));
    }

    public async Task<Robot?> SelectRobotAsync(Trigger trigger, CancellationToken cancellationToken = default)
    {
        var required = ParseTags(trigger.TargetRobotTags);
        var robots = await _robotService.ListAsync(cancellationToken);
        var activeCounts = await _triggerRepository.GetActiveJobCountsByRobotAsync(cancellationToken);

        var candidate = robots
            .Where(r => r.Status == RobotStatus.Online)
            .Where(r => required.All(tag => ParseTags(r.Tags).Contains(tag)))
            .Select(r => new
            {
                Robot = r,
                Free = r.Capacity - (activeCounts.TryGetValue(r.Id, out var c) ? c : 0),
            })
            .Where(x => x.Free > 0)
            .OrderByDescending(x => x.Free)
            .ThenByDescending(x => x.Robot.LastHeartbeat ?? DateTime.MinValue) // en taze; Priority Trigger'da tekildir
            .Select(x => x.Robot)
            .FirstOrDefault();

        return candidate;
    }

    private static HashSet<string> ParseTags(string? tags) =>
        (tags ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant())
            .ToHashSet();
}
```

Not: `Trigger.Priority` tek trigger için tekil olduğundan aday sıralamasında ayırıcı değildir; birden çok trigger'ın kaynak yarışında (ileride) kullanılacak — bu turda alan tutulur, seçim en-boş-kapasite + heartbeat ile deterministiktir.

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/RPA.Infrastructure.Tests --filter RobotDispatcherTests -v minimal`
Expected: PASS (5 test).

- [ ] **Step 6: Commit**

```bash
git add src/RPA.Domain/Interfaces/IRobotDispatcher.cs src/RPA.Infrastructure/Scheduling/RobotDispatcher.cs tests/RPA.Infrastructure.Tests/RobotDispatcherTests.cs
git commit -m "feat(infra): tag havuzu tabanli IRobotDispatcher ajan secimi"
```

---

### Task 4: Infrastructure — TriggerService dispatcher entegrasyonu

**Files:**
- Modify: `src/RPA.Infrastructure/Scheduling/TriggerService.cs`
- Modify: `tests/RPA.Infrastructure.Tests/TriggerServiceTests.cs`
- Modify: DI kaydı — `src/RPA.Infrastructure/` içindeki service collection extension (aşağıda tespit adımı)

**Interfaces:**
- Consumes: `IRobotDispatcher.SelectRobotAsync` (Task 3).
- Produces: `JobRun.AssignedRobotId` doldurulur; aday yoksa `JobRun.Status = "Pending"`.

- [ ] **Step 1: Write the failing test**

`tests/RPA.Infrastructure.Tests/TriggerServiceTests.cs` — mevcut test sınıfına ekle (var olan kurulum desenini izle; dispatcher mock'u ctor'a eklenmeli). Yeni testler:
```csharp
    [Fact]
    public async Task ExecuteTrigger_AssignsSelectedRobot()
    {
        // Arrange: mevcut test kurulumuna göre repo + workflowRunner mock'ları hazırdır.
        // Dispatcher belirli bir robot döner.
        var robot = new Robot { Id = Guid.NewGuid(), MachineName = "VM1", Status = RPA.Domain.Enums.RobotStatus.Online, Capacity = 1 };
        _dispatcher.Setup(d => d.SelectRobotAsync(It.IsAny<Trigger>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(robot);
        // ... trigger kurulumu (mevcut testlerdeki gibi manuel/parallel, çalışan JobRun yok)

        var result = await _service.ExecuteTriggerAsync(_triggerId, "manual", default);

        Assert.Equal(robot.Id, result.JobRun!.AssignedRobotId);
    }

    [Fact]
    public async Task ExecuteTrigger_NoRobot_JobRunPending()
    {
        _dispatcher.Setup(d => d.SelectRobotAsync(It.IsAny<Trigger>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync((Robot?)null);
        // ... trigger kurulumu

        var result = await _service.ExecuteTriggerAsync(_triggerId, "manual", default);

        Assert.Equal("Pending", result.JobRun!.Status);
        Assert.Null(result.JobRun.AssignedRobotId);
    }
```
Ek alan olarak test sınıfının başına `private readonly Mock<IRobotDispatcher> _dispatcher = new();` ekle ve `TriggerService` örneğini oluşturan yere `_dispatcher.Object` parametresini geçir.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/RPA.Infrastructure.Tests --filter TriggerServiceTests -v minimal`
Expected: FAIL — `TriggerService` ctor `IRobotDispatcher` almıyor; `NewJobRun` robot atamıyor.

- [ ] **Step 3: Modify TriggerService**

`src/RPA.Infrastructure/Scheduling/TriggerService.cs`:

Ctor'a alan ekle:
```csharp
    private readonly IRobotDispatcher _dispatcher;

    public TriggerService(ITriggerRepository repository, IWorkflowRunner workflowRunner,
        IRobotDispatcher dispatcher, ILogger<TriggerService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _workflowRunner = workflowRunner ?? throw new ArgumentNullException(nameof(workflowRunner));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
```

`ExecuteTriggerAsync` içinde, "hemen başlat" dalını robot seçimiyle güncelle. Mevcut satırlar:
```csharp
        // parallel ya da hiç çalışan yok: hemen başlat.
        var jobRun = NewJobRun(trigger, triggeredBy, status: "Running");
        await _repository.AddJobRunAsync(jobRun, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
```
şununla değiştir:
```csharp
        // parallel ya da hiç çalışan yok: uygun ajan seç ve başlat.
        var robot = await _dispatcher.SelectRobotAsync(trigger, cancellationToken);
        var status = robot is null ? "Pending" : "Running";
        var jobRun = NewJobRun(trigger, triggeredBy, status: status);
        jobRun.AssignedRobotId = robot?.Id;
        await _repository.AddJobRunAsync(jobRun, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        if (robot is null)
        {
            _logger.LogInformation(
                "Trigger {TriggerId}: uygun ajan yok — JobRun {JobRunId} Pending.", triggerId, jobRun.Id);
            return TriggerExecutionResult.Executed(jobRun);
        }
```
Not: `Queued` dalındaki `NewJobRun` çağrısı değişmez (overlap kuyruğu ajandan bağımsızdır — çalıştırma sırasında ele alınır). `RunAndFinalizeAsync(jobRun, ...)` çağrısı yalnız robot atandıysa (Running) çağrılır; Pending dalı yukarıda erken döner.

- [ ] **Step 4: Register IRobotDispatcher in DI**

DI kaydını bul: `grep -rn "AddScoped.*ITriggerService\|TriggerService" src/RPA.Infrastructure` ile `TriggerService`'in kaydedildiği extension dosyasını tespit et. Aynı yere ekle:
```csharp
services.AddScoped<IRobotDispatcher, RobotDispatcher>();
```
`IRobotService`'in zaten kayıtlı olduğunu doğrula (RobotsController onu kullanıyor).

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/RPA.Infrastructure.Tests --filter TriggerServiceTests -v minimal`
Expected: PASS (yeni 2 + mevcut testler).

- [ ] **Step 6: Full infra build + test**

Run: `dotnet test tests/RPA.Infrastructure.Tests -v minimal`
Expected: PASS (tümü — dispatcher parametresi eklendiği için diğer TriggerService testleri güncellenmiş olmalı).

- [ ] **Step 7: Commit**

```bash
git add src/RPA.Infrastructure/Scheduling/TriggerService.cs tests/RPA.Infrastructure.Tests/TriggerServiceTests.cs src/RPA.Infrastructure/
git commit -m "feat(infra): TriggerService uygun ajani secip AssignedRobotId doldurur (aday yoksa Pending)"
```

---

### Task 5: WebAPI — Trigger DTO alanları + GET /api/triggers

**Files:**
- Modify: `src/RPA.WebAPI/Triggers/TriggersController.cs`
- Test: `tests/RPA.WebAPI.Tests/TriggersControllerIntegrationTests.cs`

**Interfaces:**
- Consumes: `ITriggerRepository.ListTriggersAsync` (Task 2), `Trigger.TargetRobotTags`/`Priority` (Task 1).
- Produces: `GET /api/triggers?projectId=&environmentId=&isActive=` → `List<TriggerDto>`; `TriggerDto`/`CreateTriggerRequest`/`UpdateTriggerRequest` artık `TargetRobotTags`+`Priority` içerir.

- [ ] **Step 1: Write the failing test**

`tests/RPA.WebAPI.Tests/TriggersControllerIntegrationTests.cs` — mevcut test sınıfı desenini izleyerek ekle:
```csharp
    [Fact]
    public async Task Create_Then_List_ReturnsRobotTargeting()
    {
        var create = new
        {
            projectId = Guid.NewGuid(),
            workflowVersionId = Guid.NewGuid(),
            type = "Manual",
            environmentId = Guid.NewGuid(),
            isActive = true,
            targetRobotTags = "prod-vm,sap",
            priority = 3,
        };
        var createResp = await _client.PostAsJsonAsync("/api/triggers", create);
        createResp.EnsureSuccessStatusCode();

        var listResp = await _client.GetAsync("/api/triggers");
        listResp.EnsureSuccessStatusCode();
        var list = await listResp.Content.ReadFromJsonAsync<List<TriggerDtoShape>>();

        Assert.Contains(list!, t => t.TargetRobotTags == "prod-vm,sap" && t.Priority == 3);
    }

    private sealed class TriggerDtoShape
    {
        public string TargetRobotTags { get; set; } = "";
        public int Priority { get; set; }
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/RPA.WebAPI.Tests --filter TriggersControllerIntegrationTests -v minimal`
Expected: FAIL — `GET /api/triggers` yok (404) veya DTO alanları eksik.

- [ ] **Step 3: Extend DTOs and Create/Update**

`src/RPA.WebAPI/Triggers/TriggersController.cs`:

`CreateTriggerRequest`, `UpdateTriggerRequest`, `TriggerDto`'ya alan ekle:
```csharp
// CreateTriggerRequest içine:
    public string TargetRobotTags { get; set; } = "";
    public int Priority { get; set; }

// UpdateTriggerRequest içine:
    public string? TargetRobotTags { get; set; }
    public int? Priority { get; set; }

// TriggerDto içine:
    public string TargetRobotTags { get; set; } = "";
    public int Priority { get; set; }
```

`Create` metodunda `new Trigger { ... }` içine ekle:
```csharp
            TargetRobotTags = request.TargetRobotTags ?? "",
            Priority = request.Priority,
```

`Update` metodunda `Configuration` güncellemesinden sonra ekle:
```csharp
        if (request.TargetRobotTags is not null)
            trigger.TargetRobotTags = request.TargetRobotTags;
        if (request.Priority.HasValue)
            trigger.Priority = request.Priority.Value;
```

`TriggerDto.From` içine ekle:
```csharp
        TargetRobotTags = t.TargetRobotTags,
        Priority = t.Priority,
```

- [ ] **Step 4: Add GET /api/triggers list endpoint**

`TriggersController` içine yeni action ekle (GetByWorkflowVersion'ın yanına):
```csharp
    /// <summary>Tüm job tanımlarını (trigger) opsiyonel filtrelerle listeler (Studio Zamanlamalar ekranı).</summary>
    [HttpGet("triggers")]
    [ProducesResponseType(typeof(List<TriggerDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? projectId, [FromQuery] Guid? environmentId, [FromQuery] bool? isActive,
        CancellationToken ct)
    {
        var triggers = await _repository.ListTriggersAsync(projectId, environmentId, isActive, ct);
        var dtos = new List<TriggerDto>();
        foreach (var trigger in triggers)
        {
            var schedule = trigger.Type == TriggerType.Cron
                ? await _repository.FindScheduleByTriggerIdAsync(trigger.Id, ct)
                : null;
            dtos.Add(TriggerDto.From(trigger, schedule));
        }
        return Ok(dtos);
    }
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/RPA.WebAPI.Tests --filter TriggersControllerIntegrationTests -v minimal`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/RPA.WebAPI/Triggers/TriggersController.cs tests/RPA.WebAPI.Tests/TriggersControllerIntegrationTests.cs
git commit -m "feat(webapi): trigger DTO ajan hedefleme alanlari + GET /api/triggers listesi"
```

---

### Task 6: Studio — orchestrator model + servis metotları

**Files:**
- Modify: `src/RPA.Studio/src/app/orchestrator/orchestrator.models.ts`
- Modify: `src/RPA.Studio/src/app/orchestrator/orchestrator.service.ts`
- Test: `src/RPA.Studio/src/app/orchestrator/orchestrator.service.spec.ts`

**Interfaces:**
- Consumes: `GET/POST/PATCH /api/triggers`, `POST /api/triggers/{id}/fire` (Task 5 + mevcut).
- Produces: `TriggerDefinition` modeli; `listTriggers()`, `createTrigger()`, `updateTrigger()`, `fireTrigger()` servis metotları.

- [ ] **Step 1: Write the failing test**

`orchestrator.service.spec.ts` — mevcut `HttpTestingController` desenini izleyerek ekle:
```typescript
  it('listTriggers GET /api/triggers ile filtreleri geçirir', () => {
    service.listTriggers({ isActive: true }).subscribe();
    const req = httpMock.expectOne((r) => r.url === '/api/triggers');
    expect(req.request.params.get('isActive')).toBe('true');
    req.flush([]);
  });

  it('createTrigger POST /api/triggers ile gövdeyi gönderir', () => {
    const body = {
      projectId: 'p', workflowVersionId: 'w', type: 'Manual',
      environmentId: 'e', isActive: true, targetRobotTags: 'prod-vm', priority: 1,
    };
    service.createTrigger(body as any).subscribe();
    const req = httpMock.expectOne('/api/triggers');
    expect(req.request.method).toBe('POST');
    expect(req.request.body.targetRobotTags).toBe('prod-vm');
    req.flush({});
  });

  it('fireTrigger POST /api/triggers/{id}/fire çağırır', () => {
    service.fireTrigger('t1').subscribe();
    const req = httpMock.expectOne('/api/triggers/t1/fire');
    expect(req.request.method).toBe('POST');
    req.flush({});
  });
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd src/RPA.Studio && npx vitest run src/app/orchestrator/orchestrator.service.spec.ts`
Expected: FAIL — `listTriggers`/`createTrigger`/`fireTrigger` tanımlı değil.

- [ ] **Step 3: Add the model**

`orchestrator.models.ts` — sona ekle:
```typescript
export interface TriggerScheduleDto {
  cronExpression: string;
  timeZone: string;
  overlapPolicy: string;
}

export interface TriggerDefinition {
  id: string;
  workflowVersionId: string;
  type: string;
  configuration: string;
  isActive: boolean;
  targetRobotTags: string;
  priority: number;
  schedule?: TriggerScheduleDto | null;
}

export interface CreateTriggerRequest {
  projectId: string;
  workflowVersionId: string;
  type: string;
  configuration?: string;
  environmentId: string;
  isActive: boolean;
  targetRobotTags: string;
  priority: number;
  schedule?: TriggerScheduleDto | null;
}

export interface UpdateTriggerRequest {
  isActive?: boolean;
  configuration?: string;
  targetRobotTags?: string;
  priority?: number;
  schedule?: TriggerScheduleDto | null;
}

export interface TriggerListQuery {
  projectId?: string;
  environmentId?: string;
  isActive?: boolean;
}
```

- [ ] **Step 4: Add service methods**

`orchestrator.service.ts` — import satırına yeni tipleri ekle, sınıfa metotları ekle:
```typescript
  /** Tüm job tanımları (trigger) — opsiyonel filtrelerle. */
  listTriggers(query: TriggerListQuery = {}): Observable<TriggerDefinition[]> {
    let params = new HttpParams();
    if (query.projectId) params = params.set('projectId', query.projectId);
    if (query.environmentId) params = params.set('environmentId', query.environmentId);
    if (query.isActive != null) params = params.set('isActive', String(query.isActive));
    return this.http.get<TriggerDefinition[]>('/api/triggers', { params });
  }

  /** Yeni job tanımı oluşturur. */
  createTrigger(request: CreateTriggerRequest): Observable<TriggerDefinition> {
    return this.http.post<TriggerDefinition>('/api/triggers', request);
  }

  /** Job tanımını günceller (aktif/pasif, hedef tag, öncelik, schedule). */
  updateTrigger(id: string, request: UpdateTriggerRequest): Observable<TriggerDefinition> {
    return this.http.patch<TriggerDefinition>(`/api/triggers/${encodeURIComponent(id)}`, request);
  }

  /** Job'ı manuel çalıştırır. */
  fireTrigger(id: string): Observable<unknown> {
    return this.http.post(`/api/triggers/${encodeURIComponent(id)}/fire`, {});
  }
```
Import bloğuna ekle: `CreateTriggerRequest, TriggerDefinition, TriggerListQuery, UpdateTriggerRequest`.

- [ ] **Step 5: Run test to verify it passes**

Run: `cd src/RPA.Studio && npx vitest run src/app/orchestrator/orchestrator.service.spec.ts`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/RPA.Studio/src/app/orchestrator/orchestrator.models.ts src/RPA.Studio/src/app/orchestrator/orchestrator.service.ts src/RPA.Studio/src/app/orchestrator/orchestrator.service.spec.ts
git commit -m "feat(studio): orchestrator servisi trigger listeleme/olusturma/fire metotlari"
```

---

### Task 7: Studio — "Zamanlamalar" yönetim ekranı + route

**Files:**
- Create: `src/RPA.Studio/src/app/orchestrator/schedules/schedules.component.ts`
- Create: `src/RPA.Studio/src/app/orchestrator/schedules/schedules.component.html`
- Create: `src/RPA.Studio/src/app/orchestrator/schedules/schedules.component.spec.ts`
- Modify: `src/RPA.Studio/src/app/app.routes.ts`

**Interfaces:**
- Consumes: `OrchestratorService.listTriggers/createTrigger/updateTrigger/fireTrigger` (Task 6), `listRobots` (mevcut).
- Produces: `orchestrator/schedules` route + `SchedulesComponent`.

- [ ] **Step 1: Write the failing test**

`schedules.component.spec.ts`:
```typescript
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { SchedulesComponent } from './schedules.component';
import { OrchestratorService } from '../orchestrator.service';

describe('SchedulesComponent', () => {
  function setup(triggers: any[]) {
    const svc = {
      listTriggers: () => of(triggers),
      listRobots: () => of([]),
      createTrigger: () => of({}),
      updateTrigger: () => of({}),
      fireTrigger: () => of({}),
    };
    TestBed.configureTestingModule({
      imports: [SchedulesComponent],
      providers: [{ provide: OrchestratorService, useValue: svc }],
    });
    return TestBed.createComponent(SchedulesComponent);
  }

  it('yüklenince trigger listesini gösterir', () => {
    const fixture = setup([
      { id: 't1', type: 'Cron', targetRobotTags: 'prod-vm', isActive: true, priority: 0, configuration: '{}' },
    ]);
    fixture.detectChanges();
    const cmp = fixture.componentInstance;
    expect(cmp.triggers().length).toBe(1);
    expect(cmp.triggers()[0].targetRobotTags).toBe('prod-vm');
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd src/RPA.Studio && npx vitest run src/app/orchestrator/schedules/schedules.component.spec.ts`
Expected: FAIL — `SchedulesComponent` yok.

- [ ] **Step 3: Create the component**

`schedules.component.ts`:
```typescript
import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { OrchestratorService } from '../orchestrator.service';
import { CreateTriggerRequest, Robot, TriggerDefinition } from '../orchestrator.models';
import { BackHomeComponent } from '../../shared/back-home/back-home.component';

/**
 * Orchestrator Zamanlamalar ekranı: job (Trigger) tanımlarını listeler, yeni job oluşturur,
 * aktif/pasif değiştirir, manuel çalıştırır. Hangi ajanın koşacağı TargetRobotTags ile burada belirlenir.
 */
@Component({
  selector: 'app-orchestrator-schedules',
  standalone: true,
  imports: [CommonModule, FormsModule, BackHomeComponent],
  templateUrl: './schedules.component.html',
})
export class SchedulesComponent implements OnInit {
  private readonly service = inject(OrchestratorService);

  readonly triggers = signal<TriggerDefinition[]>([]);
  readonly robots = signal<Robot[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly showForm = signal(false);

  readonly triggerTypes = ['Cron', 'ApiWebhook', 'Manual'];

  form: CreateTriggerRequest = this.emptyForm();

  ngOnInit(): void {
    this.load();
    this.service.listRobots().subscribe({ next: (r) => this.robots.set(r) });
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.service.listTriggers().subscribe({
      next: (t) => {
        this.triggers.set(t);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Zamanlamalar yüklenemedi.');
        this.loading.set(false);
      },
    });
  }

  toggleForm(): void {
    this.showForm.update((v) => !v);
    if (this.showForm()) this.form = this.emptyForm();
  }

  save(): void {
    this.service.createTrigger(this.form).subscribe({
      next: () => {
        this.showForm.set(false);
        this.load();
      },
      error: () => this.error.set('Job oluşturulamadı.'),
    });
  }

  setActive(t: TriggerDefinition, isActive: boolean): void {
    this.service.updateTrigger(t.id, { isActive }).subscribe({ next: () => this.load() });
  }

  runNow(t: TriggerDefinition): void {
    this.service.fireTrigger(t.id).subscribe({ next: () => this.load() });
  }

  private emptyForm(): CreateTriggerRequest {
    return {
      projectId: '',
      workflowVersionId: '',
      type: 'Manual',
      environmentId: '',
      isActive: true,
      targetRobotTags: '',
      priority: 0,
    };
  }
}
```

- [ ] **Step 4: Create the template**

`schedules.component.html`:
```html
<app-back-home></app-back-home>
<section class="schedules">
  <header class="schedules__head">
    <h1>Zamanlamalar</h1>
    <button type="button" (click)="toggleForm()">{{ showForm() ? 'Vazgeç' : 'Yeni Job' }}</button>
  </header>

  @if (error()) {
    <p class="schedules__error">{{ error() }}</p>
  }

  @if (showForm()) {
    <form class="schedules__form" (ngSubmit)="save()">
      <label>Workflow Versiyon Id
        <input name="wv" [(ngModel)]="form.workflowVersionId" required />
      </label>
      <label>Proje Id
        <input name="pid" [(ngModel)]="form.projectId" required />
      </label>
      <label>Ortam Id
        <input name="env" [(ngModel)]="form.environmentId" required />
      </label>
      <label>Tetikleme Tipi
        <select name="type" [(ngModel)]="form.type">
          @for (t of triggerTypes; track t) { <option [value]="t">{{ t }}</option> }
        </select>
      </label>
      <label>Hedef Robot Tag'leri (virgülle)
        <input name="tags" [(ngModel)]="form.targetRobotTags" placeholder="prod-vm,sap" />
      </label>
      <label>Öncelik
        <input name="prio" type="number" [(ngModel)]="form.priority" />
      </label>
      <button type="submit">Kaydet</button>
    </form>
  }

  @if (loading()) {
    <p>Yükleniyor…</p>
  } @else {
    <table class="schedules__table">
      <thead>
        <tr><th>Tip</th><th>Hedef Ajanlar</th><th>Öncelik</th><th>Aktif</th><th>İşlem</th></tr>
      </thead>
      <tbody>
        @for (t of triggers(); track t.id) {
          <tr>
            <td>{{ t.type }}</td>
            <td>{{ t.targetRobotTags || '(herhangi)' }}</td>
            <td>{{ t.priority }}</td>
            <td>{{ t.isActive ? 'Evet' : 'Hayır' }}</td>
            <td>
              <button type="button" (click)="runNow(t)">Şimdi çalıştır</button>
              <button type="button" (click)="setActive(t, !t.isActive)">
                {{ t.isActive ? 'Pasifleştir' : 'Aktifleştir' }}
              </button>
            </td>
          </tr>
        }
      </tbody>
    </table>
  }
</section>
```

- [ ] **Step 5: Add the route**

`src/RPA.Studio/src/app/app.routes.ts` — `orchestrator/jobs` route'undan sonra ekle:
```typescript
  {
    path: 'orchestrator/schedules',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./orchestrator/schedules/schedules.component').then((m) => m.SchedulesComponent),
  },
```

- [ ] **Step 6: Run test to verify it passes**

Run: `cd src/RPA.Studio && npx vitest run src/app/orchestrator/schedules/schedules.component.spec.ts`
Expected: PASS.

- [ ] **Step 7: Build Studio to verify compilation**

Run: `cd src/RPA.Studio && npx ng build --configuration development`
Expected: SUCCESS (yeni route lazy-load derlenir).

- [ ] **Step 8: Commit**

```bash
git add src/RPA.Studio/src/app/orchestrator/schedules/ src/RPA.Studio/src/app/app.routes.ts
git commit -m "feat(studio): Zamanlamalar ekrani - job olustur/liste/fire, hedef ajan tag secimi"
```

---

### Task 8: Kontrat notu + tam derleme/test doğrulama

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Add contract-change note**

`CLAUDE.md` sonuna ekle:
```markdown
## Kontrat Değişikliği — 2026-07-14 (Job → Ajan Dispatch)

Studio workflow'larının hangi ajanda koşacağı `Trigger` katmanında tanımlanır hale geldi.
- **`Trigger`** entity: `TargetRobotTags` (virgülle ayrık tag havuzu) + `Priority` eklendi. Migration `AddTriggerRobotTargeting`.
- **Yeni arayüz:** `IRobotDispatcher.SelectRobotAsync(trigger, ct)` — Online + kapasitesi müsait +
  tag'leri kapsayan robotu seçer (en boş kapasite → heartbeat). Impl `RobotDispatcher` (Infrastructure).
- **`TriggerService`** ctor'a `IRobotDispatcher` aldı; JobRun'a `AssignedRobotId` yazar, aday yoksa `Status="Pending"`.
- **`ITriggerRepository`**: `ListTriggersAsync(projectId?, environmentId?, isActive?)` + `GetActiveJobCountsByRobotAsync()`.
- **API:** `GET /api/triggers` (job listesi); `CreateTriggerRequest`/`UpdateTriggerRequest`/`TriggerDto` `TargetRobotTags`+`Priority` içerir.
- **Studio:** `orchestrator/schedules` ekranı (job oluştur/liste/fire, hedef ajan tag seçimi).

Kapsam dışı: Agent handoff/poll protokolü (JobRun'ın ajana gerçekten teslim edilip çalıştırılması) — ayrı spec.
Etkilenen paketler yok (yeni özellik; mevcut in-process çalıştırma placeholder'ı korunur).
```

- [ ] **Step 2: Full solution build**

Run: `dotnet build`
Expected: SUCCESS.

- [ ] **Step 3: Full backend test run**

Run: `dotnet test -v minimal`
Expected: PASS (tüm katmanlar).

- [ ] **Step 4: Full Studio test run**

Run: `cd src/RPA.Studio && npx vitest run`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add CLAUDE.md
git commit -m "docs(contract): Job->Ajan dispatch kontrat notu (2026-07-14)"
```

---

## Self-Review

**Spec coverage:**
- Bölüm 1 (Domain Trigger alanları) → Task 1 ✓
- Bölüm 2 (Dispatcher + TriggerService entegrasyonu, Pending) → Task 3, 4 ✓
- Bölüm 3 (API DTO + GET /api/triggers) → Task 5 ✓
- Bölüm 4 (Studio Zamanlamalar ekranı) → Task 6, 7 ✓
- Test stratejisi → her task TDD; migration Task 1; tam doğrulama Task 8 ✓
- Etkilenen kontratlar (CLAUDE.md notu) → Task 8 ✓
- Aktif iş sayacı (kapasite için gerekli) → Task 2 ✓ (spec'te örtük; dispatcher'ın kapasite kuralı için zorunlu, eklendi)

**Placeholder scan:** Tüm kod adımları gerçek kod içerir. Task 4 Step 4 (DI kaydı) ve Step 1 (mevcut test kurulumu) grep/mevcut-desen yönergesi içerir çünkü tam dosya içeriği mevcut testin yapısına bağlıdır — yine de eklenecek satırlar/mock alanı açıkça verilmiştir.

**Type consistency:** `TargetRobotTags`/`Priority` (Task 1) tüm katmanlarda aynı adla; `SelectRobotAsync` imzası Task 3→4 tutarlı; `ListTriggersAsync`/`GetActiveJobCountsByRobotAsync` Task 2→3,5 tutarlı; TS `TriggerDefinition`/`CreateTriggerRequest` Task 6→7 tutarlı.

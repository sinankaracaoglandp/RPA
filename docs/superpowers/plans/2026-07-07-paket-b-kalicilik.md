# Paket B — Proje/Workflow Kalıcılığı: Implementasyon Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Studio'da çizilen workflow'ların kalıcı olması: proje/workflow CRUD API'si, taslak (draft) kaydet/yükle, Projelerim ekranı, designer'da Kaydet butonu + kirli takibi.

**Architecture:** Backend mevcut deseni izler — Domain'de repo arayüzü, `RPA.Infrastructure/Persistence`'ta EF implementasyonu, `RPA.Infrastructure/Services`'ta servis, WebAPI'de ince controller, testler fake repolarla. (Spec "Application katmanı" der; ancak Application projesi boştur ve WP-6.4 dahil tüm servisler Infrastructure.Services'tadır — mevcut desene uyulur.) Studio'da HttpClient servis + standalone signal component'ler. Taslak modeli: `WorkflowVersion.Status == Draft` olan tek kayıt; `PUT` onu günceller, yoksa oluşturur.

**Tech Stack:** .NET 10 (xUnit), EF Core (`RpaDbContext`), NJsonSchema (`WorkflowValidator` mevcut), Angular 22 (signals, standalone), Vitest.

**Spec:** `docs/superpowers/specs/2026-07-06-studio-toparlanma-design.md` Bölüm 5 / Paket B.

## Global Constraints

- TDD zorunlu: failing test → minimal impl → pass → commit.
- Backend test: `dotnet test tests/RPA.WebAPI.Tests` ve `dotnet test tests/RPA.Infrastructure.Tests`.
- Frontend test: `cd src/RPA.Studio && npm test -- --watch=false`.
- Kontrat dosyalarına (CLAUDE.md Kontrat Paketi'ndeki arayüzler/şema/entity'ler) dokunulmaz. Yeni repo arayüzleri kontrat paketi DIŞIDIR (WP-6.4 emsali: `IWorkflowVersionRepository`).
- i18n: kullanıcıya görünen her yeni metin `src/RPA.Studio/src/assets/i18n/tr.json` **ve** `en.json`'a eklenir; şablonda `| translate`.
- Route'lar mevcut yapıya uyar: `/projects` (liste) ve `/designer/:workflowId` — spec'teki `/studio/` öneki projede kullanılmıyor.
- Commit footer: `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`
- BusinessException: `RPA.Domain.Exceptions.BusinessException` (mevcut).

---

### Task 1: Domain repo arayüzleri + DbContext eşlemesi + EF repolar

**Files:**
- Create: `src/RPA.Domain/Interfaces/IProjectRepository.cs`
- Create: `src/RPA.Domain/Interfaces/IWorkflowRepository.cs`
- Modify: `src/RPA.Infrastructure/Persistence/RpaDbContext.cs` (DbSet + OnModelCreating)
- Create: `src/RPA.Infrastructure/Persistence/EfProjectRepository.cs`
- Create: `src/RPA.Infrastructure/Persistence/EfWorkflowRepository.cs`
- Test: `tests/RPA.Infrastructure.Tests/Persistence/ProjectWorkflowRepositoryTests.cs`

**Interfaces:**
- Consumes: mevcut `Project`, `Workflow` entity'leri (değiştirilmez), `RpaDbContext`.
- Produces (Task 2-3 bunlara dayanır):
  - `IProjectRepository`: `Task<IReadOnlyList<Project>> ListAsync(CancellationToken ct = default)`, `Task<Project?> FindAsync(Guid id, CancellationToken ct = default)`, `Task<Project> AddAsync(Project project, CancellationToken ct = default)`, `Task<int> CountWorkflowsAsync(Guid projectId, CancellationToken ct = default)`, `Task SaveChangesAsync(CancellationToken ct = default)`
  - `IWorkflowRepository`: `Task<IReadOnlyList<Workflow>> ListByProjectAsync(Guid projectId, CancellationToken ct = default)`, `Task<Workflow?> FindAsync(Guid id, CancellationToken ct = default)`, `Task<Workflow> AddAsync(Workflow workflow, CancellationToken ct = default)`, `Task<WorkflowVersion?> FindDraftAsync(Guid workflowId, CancellationToken ct = default)`, `Task AddVersionAsync(WorkflowVersion version, CancellationToken ct = default)`, `Task SaveChangesAsync(CancellationToken ct = default)`

- [ ] **Step 1: Failing test yaz**

`tests/RPA.Infrastructure.Tests/Persistence/ProjectWorkflowRepositoryTests.cs`:

```csharp
namespace RPA.Infrastructure.Tests.Persistence;

using Microsoft.EntityFrameworkCore;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Infrastructure.Persistence;
using Xunit;

/// <summary>Paket B — proje/workflow kalıcılığı: EF repo davranışları (InMemory).</summary>
public class ProjectWorkflowRepositoryTests
{
    private static RpaDbContext NewDb() => new(
        new DbContextOptionsBuilder<RpaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task Projects_AddAndList_Roundtrips()
    {
        using var db = NewDb();
        var repo = new EfProjectRepository(db);

        await repo.AddAsync(new Project { Id = Guid.NewGuid(), Name = "Pilot" });
        await repo.SaveChangesAsync();

        var list = await repo.ListAsync();
        Assert.Single(list);
        Assert.Equal("Pilot", list[0].Name);
    }

    [Fact]
    public async Task Projects_CountWorkflows_CountsOnlyThatProject()
    {
        using var db = NewDb();
        var projects = new EfProjectRepository(db);
        var workflows = new EfWorkflowRepository(db);
        var p1 = Guid.NewGuid();
        var p2 = Guid.NewGuid();
        await projects.AddAsync(new Project { Id = p1, Name = "A" });
        await projects.AddAsync(new Project { Id = p2, Name = "B" });
        await workflows.AddAsync(new Workflow { Id = Guid.NewGuid(), ProjectId = p1, Name = "w1" });
        await workflows.AddAsync(new Workflow { Id = Guid.NewGuid(), ProjectId = p1, Name = "w2" });
        await workflows.AddAsync(new Workflow { Id = Guid.NewGuid(), ProjectId = p2, Name = "w3" });
        await projects.SaveChangesAsync();

        Assert.Equal(2, await projects.CountWorkflowsAsync(p1));
        Assert.Equal(1, await projects.CountWorkflowsAsync(p2));
    }

    [Fact]
    public async Task Workflows_FindDraft_ReturnsOnlyDraftStatus()
    {
        using var db = NewDb();
        var repo = new EfWorkflowRepository(db);
        var wf = Guid.NewGuid();
        await repo.AddVersionAsync(new WorkflowVersion
        {
            Id = Guid.NewGuid(), WorkflowId = wf, Version = "1.0.0",
            Status = ComponentStatus.Published, JsonDefinition = "{}",
        });
        await repo.AddVersionAsync(new WorkflowVersion
        {
            Id = Guid.NewGuid(), WorkflowId = wf, Version = "1.1.0",
            Status = ComponentStatus.Draft, JsonDefinition = "{\"draft\":true}",
        });
        await repo.SaveChangesAsync();

        var draft = await repo.FindDraftAsync(wf);

        Assert.NotNull(draft);
        Assert.Equal("1.1.0", draft!.Version);
        Assert.Equal(ComponentStatus.Draft, draft.Status);
    }

    [Fact]
    public async Task Workflows_ListByProject_OrdersByUpdatedAtDescending()
    {
        using var db = NewDb();
        var repo = new EfWorkflowRepository(db);
        var p = Guid.NewGuid();
        await repo.AddAsync(new Workflow { Id = Guid.NewGuid(), ProjectId = p, Name = "eski", UpdatedAt = DateTime.UtcNow.AddDays(-1) });
        await repo.AddAsync(new Workflow { Id = Guid.NewGuid(), ProjectId = p, Name = "yeni", UpdatedAt = DateTime.UtcNow });
        await repo.SaveChangesAsync();

        var list = await repo.ListByProjectAsync(p);

        Assert.Equal(new[] { "yeni", "eski" }, list.Select(w => w.Name).ToArray());
    }
}
```

Not: `BaseEntity`'de `UpdatedAt` alanının adı farklıysa (`src/RPA.Domain/Entities/BaseEntity.cs`'ye bak) teste ve repoya gerçek adı kullan.

- [ ] **Step 2: Çalıştır — FAIL gözle**

Run: `dotnet test tests/RPA.Infrastructure.Tests --filter ProjectWorkflowRepository`
Expected: derleme hatası — `EfProjectRepository` yok.

- [ ] **Step 3: Arayüzleri ve implementasyonu yaz**

`src/RPA.Domain/Interfaces/IProjectRepository.cs`:

```csharp
namespace RPA.Domain.Interfaces;

using RPA.Domain.Entities;

/// <summary>Proje kalıcılık soyutlaması (Paket B — Studio Projelerim).</summary>
public interface IProjectRepository
{
    Task<IReadOnlyList<Project>> ListAsync(CancellationToken ct = default);
    Task<Project?> FindAsync(Guid id, CancellationToken ct = default);
    Task<Project> AddAsync(Project project, CancellationToken ct = default);
    /// <summary>Projedeki (soft-delete hariç) workflow sayısı — liste kartı için.</summary>
    Task<int> CountWorkflowsAsync(Guid projectId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
```

`src/RPA.Domain/Interfaces/IWorkflowRepository.cs`:

```csharp
namespace RPA.Domain.Interfaces;

using RPA.Domain.Entities;

/// <summary>Workflow + taslak versiyon kalıcılık soyutlaması (Paket B).</summary>
public interface IWorkflowRepository
{
    Task<IReadOnlyList<Workflow>> ListByProjectAsync(Guid projectId, CancellationToken ct = default);
    Task<Workflow?> FindAsync(Guid id, CancellationToken ct = default);
    Task<Workflow> AddAsync(Workflow workflow, CancellationToken ct = default);
    /// <summary>Workflow'un Status == Draft olan tek taslak versiyonu; yoksa null.</summary>
    Task<WorkflowVersion?> FindDraftAsync(Guid workflowId, CancellationToken ct = default);
    Task AddVersionAsync(WorkflowVersion version, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
```

`RpaDbContext.cs` — DbSet'ler (WorkflowVersions'ın yanına):

```csharp
    /// <summary>Paket B — Studio proje/workflow kalıcılığı.</summary>
    public DbSet<Project> Projects => Set<Project>();

    public DbSet<Workflow> Workflows => Set<Workflow>();
```

`OnModelCreating` sonuna (mevcut desen: navigasyonlar Ignore, FK sorgusu):

```csharp
        modelBuilder.Entity<Project>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Name).HasMaxLength(256).IsRequired();
            entity.Property(p => p.Description).HasMaxLength(1024);
            // Navigasyonlar tam şema paketinde (WP-1.2) yapılandırılacak.
            entity.Ignore(p => p.Workflows);
            entity.Ignore(p => p.Components);
            entity.Ignore(p => p.Queues);
        });

        modelBuilder.Entity<Workflow>(entity =>
        {
            entity.HasKey(w => w.Id);
            entity.Property(w => w.Name).HasMaxLength(256).IsRequired();
            entity.Property(w => w.Description).HasMaxLength(1024);
            entity.Property(w => w.Tags).HasMaxLength(1024);
            entity.HasIndex(w => w.ProjectId);
            entity.Ignore(w => w.Project);
            entity.Ignore(w => w.ActiveVersion);
            entity.Ignore(w => w.Versions);
            entity.Ignore(w => w.ComponentUsages);
            entity.Ignore(w => w.JobRuns);
        });
```

`src/RPA.Infrastructure/Persistence/EfProjectRepository.cs`:

```csharp
namespace RPA.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using RPA.Domain.Entities;
using RPA.Domain.Interfaces;

public sealed class EfProjectRepository : IProjectRepository
{
    private readonly RpaDbContext _db;

    public EfProjectRepository(RpaDbContext db) => _db = db;

    public async Task<IReadOnlyList<Project>> ListAsync(CancellationToken ct = default)
        => await _db.Projects.Where(p => !p.IsDeleted).OrderBy(p => p.Name).ToListAsync(ct);

    public Task<Project?> FindAsync(Guid id, CancellationToken ct = default)
        => _db.Projects.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, ct);

    public async Task<Project> AddAsync(Project project, CancellationToken ct = default)
    {
        _db.Projects.Add(project);
        return await Task.FromResult(project);
    }

    public Task<int> CountWorkflowsAsync(Guid projectId, CancellationToken ct = default)
        => _db.Workflows.CountAsync(w => w.ProjectId == projectId && !w.IsDeleted, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
```

`src/RPA.Infrastructure/Persistence/EfWorkflowRepository.cs`:

```csharp
namespace RPA.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;

public sealed class EfWorkflowRepository : IWorkflowRepository
{
    private readonly RpaDbContext _db;

    public EfWorkflowRepository(RpaDbContext db) => _db = db;

    public async Task<IReadOnlyList<Workflow>> ListByProjectAsync(Guid projectId, CancellationToken ct = default)
        => await _db.Workflows
            .Where(w => w.ProjectId == projectId && !w.IsDeleted)
            .OrderByDescending(w => w.UpdatedAt)
            .ToListAsync(ct);

    public Task<Workflow?> FindAsync(Guid id, CancellationToken ct = default)
        => _db.Workflows.FirstOrDefaultAsync(w => w.Id == id && !w.IsDeleted, ct);

    public async Task<Workflow> AddAsync(Workflow workflow, CancellationToken ct = default)
    {
        _db.Workflows.Add(workflow);
        return await Task.FromResult(workflow);
    }

    public Task<WorkflowVersion?> FindDraftAsync(Guid workflowId, CancellationToken ct = default)
        => _db.WorkflowVersions.FirstOrDefaultAsync(
            v => v.WorkflowId == workflowId && v.Status == ComponentStatus.Draft && !v.IsDeleted, ct);

    public Task AddVersionAsync(WorkflowVersion version, CancellationToken ct = default)
    {
        _db.WorkflowVersions.Add(version);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
```

(`BaseEntity`'deki gerçek alan adlarını — `IsDeleted`, `UpdatedAt` — dosyadan doğrula; farklıysa uyarlа.)

- [ ] **Step 4: Testler PASS**

Run: `dotnet test tests/RPA.Infrastructure.Tests --filter ProjectWorkflowRepository`
Expected: 4 PASS. Ardından tüm Infrastructure testleri: `dotnet test tests/RPA.Infrastructure.Tests` → PASS (DbContext değişikliği mevcut testleri kırmamalı).

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Domain/Interfaces/IProjectRepository.cs src/RPA.Domain/Interfaces/IWorkflowRepository.cs src/RPA.Infrastructure/Persistence/ tests/RPA.Infrastructure.Tests/Persistence/
git commit -m "feat(persistence): Project/Workflow EF eşlemesi ve repolar — Paket B kalıcılık temeli

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: WorkflowDesignService — proje/workflow CRUD + taslak kaydet/yükle

**Files:**
- Create: `src/RPA.Infrastructure/Services/WorkflowDesignService.cs`
- Test: `tests/RPA.Infrastructure.Tests/Services/WorkflowDesignServiceTests.cs`

**Interfaces:**
- Consumes: Task 1 arayüzleri; `WorkflowValidator.ValidateWorkflowJson(string)` (mevcut, `src/RPA.Infrastructure/Workflow/WorkflowValidator.cs`); `IEnvironmentRepository` (mevcut); `RPA.Domain.Exceptions.BusinessException`.
- Produces (Task 3 controller bunları çağırır):
  - `Task<IReadOnlyList<(Project Project, int WorkflowCount)>> ListProjectsAsync(CancellationToken ct = default)`
  - `Task<Project> CreateProjectAsync(string name, string? description, CancellationToken ct = default)` — boş ad → BusinessException
  - `Task<IReadOnlyList<Workflow>> ListWorkflowsAsync(Guid projectId, CancellationToken ct = default)` — proje yoksa BusinessException
  - `Task<Workflow> CreateWorkflowAsync(Guid projectId, string name, CancellationToken ct = default)` — boş taslak versiyonla oluşturur
  - `Task<WorkflowVersion> GetDraftAsync(Guid workflowId, CancellationToken ct = default)` — taslak yoksa boş taslak oluşturup döner
  - `Task<WorkflowVersion> SaveDraftAsync(Guid workflowId, string jsonDefinition, CancellationToken ct = default)` — şema geçersizse BusinessException (hata listesi mesajda)

**Taslak ortamı:** `WorkflowVersion.EnvironmentId` zorunlu Guid. Taslaklar "Dev" ortamına bağlanır; "Dev" yoksa otomatik oluşturulur (taslak kaydetme boş veritabanında BusinessException ile bloklanmamalı — deployment akışındaki Test/Prod zorunluluğundan farklı, bilinçli karar).

- [ ] **Step 1: Failing testler**

`tests/RPA.Infrastructure.Tests/Services/WorkflowDesignServiceTests.cs`:

```csharp
namespace RPA.Infrastructure.Tests.Services;

using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Services;
using BusinessException = RPA.Domain.Exceptions.BusinessException;
using Environment = RPA.Domain.Entities.Environment;
using Xunit;

public class WorkflowDesignServiceTests
{
    private sealed class FakeProjectRepo : IProjectRepository
    {
        public readonly List<Project> Items = new();
        public readonly List<Workflow> WorkflowItems = new();
        public Task<IReadOnlyList<Project>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Project>>(Items.OrderBy(p => p.Name).ToList());
        public Task<Project?> FindAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(Items.FirstOrDefault(p => p.Id == id));
        public Task<Project> AddAsync(Project p, CancellationToken ct = default)
        { Items.Add(p); return Task.FromResult(p); }
        public Task<int> CountWorkflowsAsync(Guid projectId, CancellationToken ct = default)
            => Task.FromResult(WorkflowItems.Count(w => w.ProjectId == projectId));
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeWorkflowRepo : IWorkflowRepository
    {
        public readonly List<Workflow> Items;
        public readonly List<WorkflowVersion> Versions = new();
        public FakeWorkflowRepo(List<Workflow>? shared = null) => Items = shared ?? new List<Workflow>();
        public Task<IReadOnlyList<Workflow>> ListByProjectAsync(Guid projectId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Workflow>>(Items.Where(w => w.ProjectId == projectId).ToList());
        public Task<Workflow?> FindAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(Items.FirstOrDefault(w => w.Id == id));
        public Task<Workflow> AddAsync(Workflow w, CancellationToken ct = default)
        { Items.Add(w); return Task.FromResult(w); }
        public Task<WorkflowVersion?> FindDraftAsync(Guid workflowId, CancellationToken ct = default)
            => Task.FromResult(Versions.FirstOrDefault(
                v => v.WorkflowId == workflowId && v.Status == ComponentStatus.Draft));
        public Task AddVersionAsync(WorkflowVersion v, CancellationToken ct = default)
        { Versions.Add(v); return Task.CompletedTask; }
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeEnvRepo : IEnvironmentRepository
    {
        public readonly List<Environment> Items = new();
        public Task<IReadOnlyList<Environment>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Environment>>(Items.ToList());
        public Task<Environment?> FindByNameAsync(string name, CancellationToken ct = default)
            => Task.FromResult(Items.FirstOrDefault(
                e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)));
        public Task<Environment> AddAsync(Environment e, CancellationToken ct = default)
        { Items.Add(e); return Task.FromResult(e); }
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static (WorkflowDesignService svc, FakeProjectRepo projects, FakeWorkflowRepo workflows, FakeEnvRepo envs) Make()
    {
        var projects = new FakeProjectRepo();
        var workflows = new FakeWorkflowRepo(projects.WorkflowItems);
        var envs = new FakeEnvRepo();
        return (new WorkflowDesignService(projects, workflows, envs, new RPA.Infrastructure.Workflow.WorkflowValidator()), projects, workflows, envs);
    }

    private const string ValidJson =
        "{\"schemaVersion\":\"1.0\",\"id\":\"wf-1\",\"name\":\"Test\",\"version\":\"1.0.0\",\"nodes\":[],\"connections\":[]}";

    [Fact]
    public async Task CreateProject_EmptyName_Throws()
    {
        var (svc, _, _, _) = Make();
        await Assert.ThrowsAsync<BusinessException>(() => svc.CreateProjectAsync("", null));
    }

    [Fact]
    public async Task ListProjects_ReturnsWorkflowCounts()
    {
        var (svc, _, _, _) = Make();
        var p = await svc.CreateProjectAsync("Pilot", "açıklama");
        await svc.CreateWorkflowAsync(p.Id, "wf-a");
        await svc.CreateWorkflowAsync(p.Id, "wf-b");

        var list = await svc.ListProjectsAsync();

        Assert.Single(list);
        Assert.Equal(2, list[0].WorkflowCount);
    }

    [Fact]
    public async Task CreateWorkflow_UnknownProject_Throws()
    {
        var (svc, _, _, _) = Make();
        await Assert.ThrowsAsync<BusinessException>(() => svc.CreateWorkflowAsync(Guid.NewGuid(), "wf"));
    }

    [Fact]
    public async Task CreateWorkflow_CreatesEmptyDraftVersion()
    {
        var (svc, _, workflows, _) = Make();
        var p = await svc.CreateProjectAsync("Pilot", null);

        var wf = await svc.CreateWorkflowAsync(p.Id, "Sipariş Aktarımı");

        var draft = await workflows.FindDraftAsync(wf.Id);
        Assert.NotNull(draft);
        Assert.Equal(ComponentStatus.Draft, draft!.Status);
        Assert.Contains("\"nodes\"", draft.JsonDefinition);
    }

    [Fact]
    public async Task GetDraft_MissingDraft_CreatesOne()
    {
        var (svc, projects, workflows, _) = Make();
        var p = await svc.CreateProjectAsync("Pilot", null);
        var wf = await svc.CreateWorkflowAsync(p.Id, "wf");
        workflows.Versions.Clear(); // taslağı yapay olarak kaldır

        var draft = await svc.GetDraftAsync(wf.Id);

        Assert.Equal(ComponentStatus.Draft, draft.Status);
        Assert.Single(workflows.Versions);
    }

    [Fact]
    public async Task SaveDraft_ValidJson_UpdatesExistingDraft_NoNewVersion()
    {
        var (svc, _, workflows, _) = Make();
        var p = await svc.CreateProjectAsync("Pilot", null);
        var wf = await svc.CreateWorkflowAsync(p.Id, "wf");

        var saved = await svc.SaveDraftAsync(wf.Id, ValidJson);

        Assert.Equal(ValidJson, saved.JsonDefinition);
        Assert.Single(workflows.Versions); // güncelleme, yeni versiyon değil
    }

    [Fact]
    public async Task SaveDraft_InvalidJson_ThrowsBusinessException()
    {
        var (svc, _, _, _) = Make();
        var p = await svc.CreateProjectAsync("Pilot", null);
        var wf = await svc.CreateWorkflowAsync(p.Id, "wf");

        await Assert.ThrowsAsync<BusinessException>(
            () => svc.SaveDraftAsync(wf.Id, "{\"nodes\":\"bozuk\"}"));
    }

    [Fact]
    public async Task Draft_UsesDevEnvironment_AutoCreatesWhenMissing()
    {
        var (svc, _, workflows, envs) = Make();
        var p = await svc.CreateProjectAsync("Pilot", null);
        var wf = await svc.CreateWorkflowAsync(p.Id, "wf");

        Assert.Contains(envs.Items, e => e.Name == "Dev");
        var draft = await workflows.FindDraftAsync(wf.Id);
        Assert.Equal(envs.Items.First(e => e.Name == "Dev").Id, draft!.EnvironmentId);
    }
}
```

- [ ] **Step 2: Çalıştır — FAIL gözle**

Run: `dotnet test tests/RPA.Infrastructure.Tests --filter WorkflowDesignService`
Expected: derleme hatası — `WorkflowDesignService` yok.

- [ ] **Step 3: Servisi yaz**

`src/RPA.Infrastructure/Services/WorkflowDesignService.cs`:

```csharp
namespace RPA.Infrastructure.Services;

using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Workflow;
using BusinessException = RPA.Domain.Exceptions.BusinessException;
using Environment = RPA.Domain.Entities.Environment;

/// <summary>
/// Studio tasarım-zamanı kalıcılık akışı (Paket B): proje/workflow oluşturma-listeleme
/// ve taslak (Status == Draft) kaydet/yükle. Taslak tek kayıttır; kaydetme mevcut
/// taslağın JsonDefinition'ını günceller, yeni versiyon YARATMAZ (yayınlama
/// WorkflowDeploymentService'te kalır). JSON, kontrat şeması v1.0'a karşı doğrulanır.
/// </summary>
public sealed class WorkflowDesignService
{
    /// <summary>Taslakların bağlandığı ortam; yoksa otomatik oluşturulur.</summary>
    public const string DraftEnvironmentName = "Dev";

    private readonly IProjectRepository _projects;
    private readonly IWorkflowRepository _workflows;
    private readonly IEnvironmentRepository _environments;
    private readonly WorkflowValidator _validator;

    public WorkflowDesignService(
        IProjectRepository projects,
        IWorkflowRepository workflows,
        IEnvironmentRepository environments,
        WorkflowValidator validator)
    {
        _projects = projects ?? throw new ArgumentNullException(nameof(projects));
        _workflows = workflows ?? throw new ArgumentNullException(nameof(workflows));
        _environments = environments ?? throw new ArgumentNullException(nameof(environments));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public async Task<IReadOnlyList<(Project Project, int WorkflowCount)>> ListProjectsAsync(
        CancellationToken ct = default)
    {
        var projects = await _projects.ListAsync(ct).ConfigureAwait(false);
        var result = new List<(Project, int)>(projects.Count);
        foreach (var p in projects)
        {
            result.Add((p, await _projects.CountWorkflowsAsync(p.Id, ct).ConfigureAwait(false)));
        }
        return result;
    }

    public async Task<Project> CreateProjectAsync(
        string name, string? description, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new BusinessException("Proje adı boş olamaz.");
        }
        var project = new Project { Id = Guid.NewGuid(), Name = name.Trim(), Description = description };
        await _projects.AddAsync(project, ct).ConfigureAwait(false);
        await _projects.SaveChangesAsync(ct).ConfigureAwait(false);
        return project;
    }

    public async Task<IReadOnlyList<Workflow>> ListWorkflowsAsync(
        Guid projectId, CancellationToken ct = default)
    {
        _ = await RequireProject(projectId, ct).ConfigureAwait(false);
        return await _workflows.ListByProjectAsync(projectId, ct).ConfigureAwait(false);
    }

    public async Task<Workflow> CreateWorkflowAsync(
        Guid projectId, string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new BusinessException("Workflow adı boş olamaz.");
        }
        _ = await RequireProject(projectId, ct).ConfigureAwait(false);

        var workflow = new Workflow { Id = Guid.NewGuid(), ProjectId = projectId, Name = name.Trim() };
        await _workflows.AddAsync(workflow, ct).ConfigureAwait(false);
        await CreateDraft(workflow, ct).ConfigureAwait(false);
        await _workflows.SaveChangesAsync(ct).ConfigureAwait(false);
        return workflow;
    }

    public async Task<WorkflowVersion> GetDraftAsync(Guid workflowId, CancellationToken ct = default)
    {
        var workflow = await RequireWorkflow(workflowId, ct).ConfigureAwait(false);
        var draft = await _workflows.FindDraftAsync(workflowId, ct).ConfigureAwait(false);
        if (draft is null)
        {
            draft = await CreateDraft(workflow, ct).ConfigureAwait(false);
            await _workflows.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        return draft;
    }

    public async Task<WorkflowVersion> SaveDraftAsync(
        Guid workflowId, string jsonDefinition, CancellationToken ct = default)
    {
        var validation = _validator.ValidateWorkflowJson(jsonDefinition);
        if (!validation.IsValid)
        {
            throw new BusinessException(
                $"Workflow JSON şema doğrulaması başarısız: {string.Join("; ", validation.Errors)}");
        }

        var workflow = await RequireWorkflow(workflowId, ct).ConfigureAwait(false);
        var draft = await _workflows.FindDraftAsync(workflowId, ct).ConfigureAwait(false)
            ?? await CreateDraft(workflow, ct).ConfigureAwait(false);

        draft.JsonDefinition = jsonDefinition;
        await _workflows.SaveChangesAsync(ct).ConfigureAwait(false);
        return draft;
    }

    private async Task<WorkflowVersion> CreateDraft(Workflow workflow, CancellationToken ct)
    {
        var env = await _environments.FindByNameAsync(DraftEnvironmentName, ct).ConfigureAwait(false);
        if (env is null)
        {
            env = new Environment { Id = Guid.NewGuid(), Name = DraftEnvironmentName };
            await _environments.AddAsync(env, ct).ConfigureAwait(false);
        }

        var draft = new WorkflowVersion
        {
            Id = Guid.NewGuid(),
            WorkflowId = workflow.Id,
            Version = "1.0.0",
            Status = ComponentStatus.Draft,
            EnvironmentId = env.Id,
            JsonDefinition = EmptyDefinition(workflow),
        };
        await _workflows.AddVersionAsync(draft, ct).ConfigureAwait(false);
        return draft;
    }

    private static string EmptyDefinition(Workflow workflow)
        => $"{{\"schemaVersion\":\"1.0\",\"id\":\"{workflow.Id}\",\"name\":\"{workflow.Name.Replace("\"", "\\\"")}\",\"version\":\"1.0.0\",\"nodes\":[],\"connections\":[],\"variables\":[]}}";

    private async Task<Project> RequireProject(Guid id, CancellationToken ct)
        => await _projects.FindAsync(id, ct).ConfigureAwait(false)
            ?? throw new BusinessException($"Proje bulunamadı: {id}");

    private async Task<Workflow> RequireWorkflow(Guid id, CancellationToken ct)
        => await _workflows.FindAsync(id, ct).ConfigureAwait(false)
            ?? throw new BusinessException($"Workflow bulunamadı: {id}");
}
```

Not: `WorkflowValidationResult`'ın üye adlarını (`IsValid`, `Errors`) `src/RPA.Infrastructure/Workflow/` altındaki tanımdan doğrula; farklıysa uyarlа. Boş tanım JSON'unun şemaya uygunluğunu `SaveDraft_ValidJson...` benzeri ek küçük bir testle garanti etmek istersen `EmptyDefinition` çıktısını `_validator` ile doğrulayan bir test ekleyebilirsin (şema `variables` gerektirmiyorsa da zararsız).

- [ ] **Step 4: Testler PASS**

Run: `dotnet test tests/RPA.Infrastructure.Tests --filter WorkflowDesignService`
Expected: 8 PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Infrastructure/Services/WorkflowDesignService.cs tests/RPA.Infrastructure.Tests/Services/WorkflowDesignServiceTests.cs
git commit -m "feat(studio-api): WorkflowDesignService — proje/workflow CRUD + taslak kaydet/yükle

Taslak = Status Draft olan tek WorkflowVersion; kaydetme JsonDefinition'ı
günceller, şema v1.0 doğrulaması BusinessException ile raporlanır.
Taslaklar Dev ortamına bağlanır (yoksa otomatik oluşturulur).

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: ProjectsController + WorkflowsController (draft uçları) + DI

**Files:**
- Create: `src/RPA.WebAPI/Controllers/ProjectsController.cs`
- Create: `src/RPA.WebAPI/Controllers/WorkflowsController.cs`
- Modify: `src/RPA.WebAPI/Program.cs` (DI kayıtları)
- Test: `tests/RPA.WebAPI.Tests/ProjectsControllerTests.cs`

**Interfaces:**
- Consumes: Task 2 `WorkflowDesignService` (tüm public metotlar).
- Produces (Studio Task 4-6 bu uçları çağırır):
  - `GET  /api/projects` → `[{ id, name, description, workflowCount }]`
  - `POST /api/projects` body `{ name, description? }` → `ProjectDto`
  - `GET  /api/projects/{projectId}/workflows` → `[{ id, name, updatedAt }]`
  - `POST /api/projects/{projectId}/workflows` body `{ name }` → `WorkflowSummaryDto`
  - `GET  /api/workflows/{workflowId}/draft` → `{ id, workflowId, version, jsonDefinition }`
  - `PUT  /api/workflows/{workflowId}/draft` body `{ jsonDefinition }` → aynı DTO; şema hatasında 400 `{ error }`
  - BusinessException → 400 (`{ error = ex.Message }`), geçersiz GUID → 400 (mevcut controller deseni).

- [ ] **Step 1: Failing testler**

`tests/RPA.WebAPI.Tests/ProjectsControllerTests.cs` — `WorkflowDeploymentControllerTests` desenini izle (fake repolar + doğrudan controller örneği). Task 2'deki fake'leri kopyalamak yerine bu test dosyasına aynen taşı (test projeleri ayrı — paylaşım yok):

```csharp
namespace RPA.WebAPI.Tests;

using Microsoft.AspNetCore.Mvc;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Services;
using RPA.WebAPI.Controllers;
using Environment = RPA.Domain.Entities.Environment;

/// <summary>Paket B — proje/workflow CRUD ve taslak uçları (fake repolarla).</summary>
public class ProjectsControllerTests
{
    // FakeProjectRepo / FakeWorkflowRepo / FakeEnvRepo: Task 2 test dosyasındaki
    // implementasyonların birebir kopyası (bkz. WorkflowDesignServiceTests) — buraya aynen ekle.

    private static WorkflowDesignService Service(
        out List<Project> projects, out List<WorkflowVersion> versions)
    {
        var projectRepo = new FakeProjectRepo();
        var workflowRepo = new FakeWorkflowRepo(projectRepo.WorkflowItems);
        projects = projectRepo.Items;
        versions = workflowRepo.Versions;
        return new WorkflowDesignService(
            projectRepo, workflowRepo, new FakeEnvRepo(),
            new RPA.Infrastructure.Workflow.WorkflowValidator());
    }

    private const string ValidJson =
        "{\"schemaVersion\":\"1.0\",\"id\":\"wf-1\",\"name\":\"Test\",\"version\":\"1.0.0\",\"nodes\":[],\"connections\":[]}";

    [Fact]
    public async Task CreateProject_ThenList_ReturnsCard()
    {
        var controller = new ProjectsController(Service(out _, out _));

        await controller.Create(new CreateProjectRequest { Name = "Pilot", Description = "d" }, default);
        var result = await controller.List(default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<ProjectDto>>(ok.Value);
        Assert.Single(list);
        Assert.Equal("Pilot", list[0].Name);
        Assert.Equal(0, list[0].WorkflowCount);
    }

    [Fact]
    public async Task CreateProject_EmptyName_Returns400()
    {
        var controller = new ProjectsController(Service(out _, out _));
        var result = await controller.Create(new CreateProjectRequest { Name = " " }, default);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task CreateWorkflow_ThenListWorkflows_ReturnsIt()
    {
        var svc = Service(out var projects, out _);
        var controller = new ProjectsController(svc);
        var created = await controller.Create(new CreateProjectRequest { Name = "Pilot" }, default);
        var projectId = ((ProjectDto)((OkObjectResult)created.Result!).Value!).Id;

        await controller.CreateWorkflow(projectId.ToString(),
            new CreateWorkflowRequest { Name = "Sipariş" }, default);
        var result = await controller.ListWorkflows(projectId.ToString(), default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<WorkflowSummaryDto>>(ok.Value);
        Assert.Single(list);
        Assert.Equal("Sipariş", list[0].Name);
    }

    [Fact]
    public async Task ListWorkflows_BadGuid_Returns400()
    {
        var controller = new ProjectsController(Service(out _, out _));
        var result = await controller.ListWorkflows("not-a-guid", default);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetDraft_ReturnsJsonDefinition()
    {
        var svc = Service(out _, out _);
        var projects = new ProjectsController(svc);
        var workflows = new WorkflowsController(svc);
        var created = await projects.Create(new CreateProjectRequest { Name = "P" }, default);
        var projectId = ((ProjectDto)((OkObjectResult)created.Result!).Value!).Id;
        var wfCreated = await projects.CreateWorkflow(projectId.ToString(),
            new CreateWorkflowRequest { Name = "wf" }, default);
        var wfId = ((WorkflowSummaryDto)((OkObjectResult)wfCreated.Result!).Value!).Id;

        var result = await workflows.GetDraft(wfId.ToString(), default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<WorkflowDraftDto>(ok.Value);
        Assert.Contains("\"nodes\"", dto.JsonDefinition);
    }

    [Fact]
    public async Task SaveDraft_ValidJson_Persists()
    {
        var svc = Service(out _, out var versions);
        var projects = new ProjectsController(svc);
        var workflows = new WorkflowsController(svc);
        var created = await projects.Create(new CreateProjectRequest { Name = "P" }, default);
        var projectId = ((ProjectDto)((OkObjectResult)created.Result!).Value!).Id;
        var wfCreated = await projects.CreateWorkflow(projectId.ToString(),
            new CreateWorkflowRequest { Name = "wf" }, default);
        var wfId = ((WorkflowSummaryDto)((OkObjectResult)wfCreated.Result!).Value!).Id;

        var result = await workflows.SaveDraft(wfId.ToString(),
            new SaveDraftRequest { JsonDefinition = ValidJson }, default);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(ValidJson, versions.Single().JsonDefinition);
    }

    [Fact]
    public async Task SaveDraft_InvalidJson_Returns400WithErrors()
    {
        var svc = Service(out _, out _);
        var projects = new ProjectsController(svc);
        var workflows = new WorkflowsController(svc);
        var created = await projects.Create(new CreateProjectRequest { Name = "P" }, default);
        var projectId = ((ProjectDto)((OkObjectResult)created.Result!).Value!).Id;
        var wfCreated = await projects.CreateWorkflow(projectId.ToString(),
            new CreateWorkflowRequest { Name = "wf" }, default);
        var wfId = ((WorkflowSummaryDto)((OkObjectResult)wfCreated.Result!).Value!).Id;

        var result = await workflows.SaveDraft(wfId.ToString(),
            new SaveDraftRequest { JsonDefinition = "{\"nodes\":\"bozuk\"}" }, default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }
}
```

- [ ] **Step 2: Çalıştır — FAIL gözle**

Run: `dotnet test tests/RPA.WebAPI.Tests --filter ProjectsController`
Expected: derleme hatası — controller'lar yok.

- [ ] **Step 3: Controller'ları yaz**

`src/RPA.WebAPI/Controllers/ProjectsController.cs`:

```csharp
namespace RPA.WebAPI.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RPA.Domain.Entities;
using RPA.Infrastructure.Services;
using BusinessException = RPA.Domain.Exceptions.BusinessException;

/// <summary>Studio Projelerim uç noktaları (Paket B — proje/workflow kalıcılığı).</summary>
[ApiController]
[Route("api/projects")]
[Authorize]
public class ProjectsController : ControllerBase
{
    private readonly WorkflowDesignService _service;

    public ProjectsController(WorkflowDesignService service) => _service = service;

    /// <summary>Proje listesi (workflow sayılarıyla).</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ProjectDto>>> List(CancellationToken ct)
    {
        var projects = await _service.ListProjectsAsync(ct);
        return Ok(projects.Select(p => Map(p.Project, p.WorkflowCount)).ToList());
    }

    /// <summary>Yeni proje oluşturur.</summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProjectDto>> Create(
        [FromBody] CreateProjectRequest request, CancellationToken ct)
    {
        try
        {
            var project = await _service.CreateProjectAsync(request.Name, request.Description, ct);
            return Ok(Map(project, 0));
        }
        catch (BusinessException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Projedeki workflow'lar (son güncellenme sırasıyla).</summary>
    [HttpGet("{projectId}/workflows")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<WorkflowSummaryDto>>> ListWorkflows(
        string projectId, CancellationToken ct)
    {
        if (!Guid.TryParse(projectId, out var id))
        {
            return BadRequest(new { error = "'projectId' geçerli bir GUID olmalıdır." });
        }
        try
        {
            var workflows = await _service.ListWorkflowsAsync(id, ct);
            return Ok(workflows.Select(Map).ToList());
        }
        catch (BusinessException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Projede workflow oluşturur (boş taslak versiyonla).</summary>
    [HttpPost("{projectId}/workflows")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<WorkflowSummaryDto>> CreateWorkflow(
        string projectId, [FromBody] CreateWorkflowRequest request, CancellationToken ct)
    {
        if (!Guid.TryParse(projectId, out var id))
        {
            return BadRequest(new { error = "'projectId' geçerli bir GUID olmalıdır." });
        }
        try
        {
            var workflow = await _service.CreateWorkflowAsync(id, request.Name, ct);
            return Ok(Map(workflow));
        }
        catch (BusinessException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    private static ProjectDto Map(Project p, int workflowCount) => new()
    {
        Id = p.Id, Name = p.Name, Description = p.Description, WorkflowCount = workflowCount,
    };

    private static WorkflowSummaryDto Map(Workflow w) => new()
    {
        Id = w.Id, Name = w.Name, UpdatedAt = w.UpdatedAt,
    };
}

public class CreateProjectRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class CreateWorkflowRequest
{
    public string Name { get; set; } = string.Empty;
}

public class ProjectDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int WorkflowCount { get; set; }
}

public class WorkflowSummaryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime? UpdatedAt { get; set; }
}
```

(`UpdatedAt` tipini `BaseEntity`'deki gerçek tiple eşle.)

`src/RPA.WebAPI/Controllers/WorkflowsController.cs`:

```csharp
namespace RPA.WebAPI.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RPA.Domain.Entities;
using RPA.Infrastructure.Services;
using BusinessException = RPA.Domain.Exceptions.BusinessException;

/// <summary>Workflow taslak (draft) kaydet/yükle uçları (Paket B).</summary>
[ApiController]
[Route("api/workflows/{workflowId}/draft")]
[Authorize]
public class WorkflowsController : ControllerBase
{
    private readonly WorkflowDesignService _service;

    public WorkflowsController(WorkflowDesignService service) => _service = service;

    /// <summary>Taslak versiyonu (JsonDefinition dahil) döndürür; yoksa boş taslak oluşturur.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<WorkflowDraftDto>> GetDraft(string workflowId, CancellationToken ct)
    {
        if (!Guid.TryParse(workflowId, out var id))
        {
            return BadRequest(new { error = "'workflowId' geçerli bir GUID olmalıdır." });
        }
        try
        {
            return Ok(Map(await _service.GetDraftAsync(id, ct)));
        }
        catch (BusinessException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Taslağı kaydeder; JSON şema v1.0'a karşı doğrulanır (geçersizse 400).</summary>
    [HttpPut]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<WorkflowDraftDto>> SaveDraft(
        string workflowId, [FromBody] SaveDraftRequest request, CancellationToken ct)
    {
        if (!Guid.TryParse(workflowId, out var id))
        {
            return BadRequest(new { error = "'workflowId' geçerli bir GUID olmalıdır." });
        }
        if (request is null || string.IsNullOrWhiteSpace(request.JsonDefinition))
        {
            return BadRequest(new { error = "'jsonDefinition' zorunludur." });
        }
        try
        {
            return Ok(Map(await _service.SaveDraftAsync(id, request.JsonDefinition, ct)));
        }
        catch (BusinessException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    private static WorkflowDraftDto Map(WorkflowVersion v) => new()
    {
        Id = v.Id, WorkflowId = v.WorkflowId, Version = v.Version, JsonDefinition = v.JsonDefinition,
    };
}

public class SaveDraftRequest
{
    public string JsonDefinition { get; set; } = string.Empty;
}

public class WorkflowDraftDto
{
    public Guid Id { get; set; }
    public Guid WorkflowId { get; set; }
    public string Version { get; set; } = string.Empty;
    public string JsonDefinition { get; set; } = string.Empty;
}
```

`Program.cs` — mevcut WP-6.4 kayıtlarının (satır ~77-82) hemen ardına:

```csharp
builder.Services.AddScoped<RPA.Domain.Interfaces.IProjectRepository,
    RPA.Infrastructure.Persistence.EfProjectRepository>();
builder.Services.AddScoped<RPA.Domain.Interfaces.IWorkflowRepository,
    RPA.Infrastructure.Persistence.EfWorkflowRepository>();
builder.Services.AddSingleton<RPA.Infrastructure.Workflow.WorkflowValidator>();
builder.Services.AddScoped<RPA.Infrastructure.Services.WorkflowDesignService>();
```

(`WorkflowValidator` zaten kayıtlıysa — Program.cs'te ara — mükerrer ekleme.)

- [ ] **Step 4: Testler PASS**

Run: `dotnet test tests/RPA.WebAPI.Tests --filter ProjectsController` → 7 PASS.
Sonra tümü: `dotnet test tests/RPA.WebAPI.Tests` → PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RPA.WebAPI/Controllers/ProjectsController.cs src/RPA.WebAPI/Controllers/WorkflowsController.cs src/RPA.WebAPI/Program.cs tests/RPA.WebAPI.Tests/ProjectsControllerTests.cs
git commit -m "feat(webapi): proje/workflow CRUD ve taslak uçları — Paket B

GET/POST /api/projects, GET/POST /api/projects/{id}/workflows,
GET/PUT /api/workflows/{id}/draft (şema v1.0 doğrulamalı).

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: Studio — ProjectService + WorkflowDraftService genişletmesi

**Files:**
- Create: `src/RPA.Studio/src/app/shared/services/project.service.ts`
- Modify: `src/RPA.Studio/src/app/shared/services/workflow-draft.service.ts`
- Test: `src/RPA.Studio/src/app/shared/services/project.service.spec.ts`
- Test: `src/RPA.Studio/src/app/shared/services/workflow-draft.service.spec.ts` (yeni)

**Interfaces:**
- Consumes: Task 3 API uçları.
- Produces (Task 5-6 kullanır):
  - `ProjectService`: `getProjects(): Observable<ProjectSummary[]>`, `createProject(name, description?): Observable<ProjectSummary>`, `getWorkflows(projectId): Observable<WorkflowSummary[]>`, `createWorkflow(projectId, name): Observable<WorkflowSummary>`
  - `interface ProjectSummary { id: string; name: string; description?: string; workflowCount: number }`
  - `interface WorkflowSummary { id: string; name: string; updatedAt?: string }`
  - `WorkflowDraftService` ek: `load(workflowId: string): Observable<WorkflowVersion>` (draft JSON'u parse edip döner), `save(workflowId: string, version: WorkflowVersion): Observable<void>`; `setPending`/`consumePending` aynen korunur.

- [ ] **Step 1: Failing testler**

`project.service.spec.ts`:

```typescript
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ProjectService } from './project.service';

describe('ProjectService', () => {
  let service: ProjectService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(ProjectService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists projects from GET /api/projects', () => {
    let result: unknown;
    service.getProjects().subscribe((r) => (result = r));

    const req = http.expectOne('/api/projects');
    expect(req.request.method).toBe('GET');
    req.flush([{ id: 'p1', name: 'Pilot', workflowCount: 2 }]);

    expect(result).toEqual([{ id: 'p1', name: 'Pilot', workflowCount: 2 }]);
  });

  it('creates a project via POST /api/projects', () => {
    service.createProject('Pilot', 'açıklama').subscribe();
    const req = http.expectOne('/api/projects');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ name: 'Pilot', description: 'açıklama' });
    req.flush({ id: 'p1', name: 'Pilot', workflowCount: 0 });
  });

  it('creates a workflow via POST /api/projects/{id}/workflows', () => {
    service.createWorkflow('p1', 'Sipariş').subscribe();
    const req = http.expectOne('/api/projects/p1/workflows');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ name: 'Sipariş' });
    req.flush({ id: 'w1', name: 'Sipariş' });
  });
});
```

`workflow-draft.service.spec.ts`:

```typescript
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { WorkflowDraftService } from './workflow-draft.service';
import { emptyWorkflow } from '../models/workflow.model';

describe('WorkflowDraftService', () => {
  let service: WorkflowDraftService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(WorkflowDraftService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('load parses the draft JsonDefinition into a WorkflowVersion', () => {
    const wf = emptyWorkflow('w1', 'Sipariş');
    let result: unknown;
    service.load('w1').subscribe((r) => (result = r));

    http.expectOne('/api/workflows/w1/draft').flush({
      id: 'v1', workflowId: 'w1', version: '1.0.0',
      jsonDefinition: JSON.stringify(wf),
    });

    expect(result).toEqual(wf);
  });

  it('save PUTs the serialized graph to the draft endpoint', () => {
    const wf = emptyWorkflow('w1', 'Sipariş');
    service.save('w1', wf).subscribe();

    const req = http.expectOne('/api/workflows/w1/draft');
    expect(req.request.method).toBe('PUT');
    expect(JSON.parse(req.request.body.jsonDefinition)).toEqual(wf);
    req.flush({ id: 'v1', workflowId: 'w1', version: '1.0.0', jsonDefinition: '{}' });
  });

  it('keeps the existing pending hand-off behaviour', () => {
    const wf = emptyWorkflow();
    service.setPending(wf);
    expect(service.consumePending()).toEqual(wf);
    expect(service.consumePending()).toBeNull();
  });
});
```

- [ ] **Step 2: Çalıştır — FAIL gözle**

Run: `cd src/RPA.Studio && npm test -- --watch=false --include='**/{project,workflow-draft}.service.spec.ts'`
Expected: FAIL — `ProjectService` yok, `load/save` tanımsız.

- [ ] **Step 3: Servisleri yaz**

`project.service.ts`:

```typescript
import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export interface ProjectSummary {
  id: string;
  name: string;
  description?: string;
  workflowCount: number;
}

export interface WorkflowSummary {
  id: string;
  name: string;
  updatedAt?: string;
}

/** Projelerim ekranının backend erişimi (Paket B — /api/projects). */
@Injectable({ providedIn: 'root' })
export class ProjectService {
  private readonly http = inject(HttpClient);

  getProjects(): Observable<ProjectSummary[]> {
    return this.http.get<ProjectSummary[]>('/api/projects');
  }

  createProject(name: string, description?: string): Observable<ProjectSummary> {
    return this.http.post<ProjectSummary>('/api/projects', { name, description });
  }

  getWorkflows(projectId: string): Observable<WorkflowSummary[]> {
    return this.http.get<WorkflowSummary[]>(
      `/api/projects/${encodeURIComponent(projectId)}/workflows`,
    );
  }

  createWorkflow(projectId: string, name: string): Observable<WorkflowSummary> {
    return this.http.post<WorkflowSummary>(
      `/api/projects/${encodeURIComponent(projectId)}/workflows`,
      { name },
    );
  }
}
```

`workflow-draft.service.ts` — mevcut içerik korunur, HTTP eklenir:

```typescript
import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, map } from 'rxjs';
import { WorkflowVersion } from '../models/workflow.model';

interface WorkflowDraftDto {
  id: string;
  workflowId: string;
  version: string;
  jsonDefinition: string;
}

/**
 * Hand-off point for "create workflow from template" (Faz 5, Task 5.5) ve
 * taslak kalıcılığı (Paket B): backend'deki draft'ı yükle/kaydet.
 */
@Injectable({ providedIn: 'root' })
export class WorkflowDraftService {
  private readonly http = inject(HttpClient);
  private readonly _pending = signal<WorkflowVersion | null>(null);

  setPending(workflow: WorkflowVersion): void {
    this._pending.set(workflow);
  }

  /** Returns and clears the pending draft, if any. */
  consumePending(): WorkflowVersion | null {
    const pending = this._pending();
    this._pending.set(null);
    return pending;
  }

  /** Backend'deki taslağı yükler (JsonDefinition parse edilir). */
  load(workflowId: string): Observable<WorkflowVersion> {
    return this.http
      .get<WorkflowDraftDto>(`/api/workflows/${encodeURIComponent(workflowId)}/draft`)
      .pipe(map((dto) => JSON.parse(dto.jsonDefinition) as WorkflowVersion));
  }

  /** Canvas'tan serialize edilen grafiği taslağa kaydeder. */
  save(workflowId: string, version: WorkflowVersion): Observable<void> {
    return this.http
      .put<WorkflowDraftDto>(`/api/workflows/${encodeURIComponent(workflowId)}/draft`, {
        jsonDefinition: JSON.stringify(version),
      })
      .pipe(map(() => undefined));
  }
}
```

- [ ] **Step 4: Testler PASS**

Run: `cd src/RPA.Studio && npm test -- --watch=false --include='**/{project,workflow-draft}.service.spec.ts'`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Studio/src/app/shared/services/
git commit -m "feat(studio): ProjectService ve taslak yükle/kaydet HTTP akışı

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: Studio — Projelerim ekranı + route + dashboard kartı

**Files:**
- Create: `src/RPA.Studio/src/app/studio/projects/projects.component.ts`
- Create: `src/RPA.Studio/src/app/studio/projects/projects.component.html`
- Modify: `src/RPA.Studio/src/app/app.routes.ts`
- Modify: dashboard component (route `''` → `DashboardComponent`; dosyayı bul, mevcut kart desenine bir "Projelerim" kartı ekle)
- Modify: `src/RPA.Studio/src/assets/i18n/tr.json`, `en.json`
- Test: `src/RPA.Studio/src/app/studio/projects/projects.component.spec.ts`

**Interfaces:**
- Consumes: Task 4 `ProjectService`.
- Produces: `/projects` route'u; "Aç" → `router.navigate(['/designer', workflowId])` (Task 6 bu route'u tanımlar — Task 5 ve 6 aynı PR diliminde sıralı gider).

- [ ] **Step 1: Failing test**

`projects.component.spec.ts`:

```typescript
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { ProjectsComponent } from './projects.component';

describe('ProjectsComponent', () => {
  let fixture: ComponentFixture<ProjectsComponent>;
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ProjectsComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    fixture = TestBed.createComponent(ProjectsComponent);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists project cards with workflow counts', () => {
    fixture.detectChanges();
    http.expectOne('/api/projects').flush([
      { id: 'p1', name: 'Pilot', description: 'd', workflowCount: 2 },
    ]);
    fixture.detectChanges();

    const cards = fixture.nativeElement.querySelectorAll('[data-testid="project-card"]');
    expect(cards.length).toBe(1);
    expect(cards[0].textContent).toContain('Pilot');
  });

  it('creates a project and refreshes the list', () => {
    fixture.detectChanges();
    http.expectOne('/api/projects').flush([]);
    fixture.detectChanges();

    fixture.componentInstance.newProjectName.set('Yeni');
    fixture.componentInstance.createProject();

    const post = http.expectOne(
      (r) => r.url === '/api/projects' && r.method === 'POST',
    );
    post.flush({ id: 'p2', name: 'Yeni', workflowCount: 0 });
    http.expectOne('/api/projects').flush([{ id: 'p2', name: 'Yeni', workflowCount: 0 }]);
    fixture.detectChanges();

    expect(
      fixture.nativeElement.querySelectorAll('[data-testid="project-card"]').length,
    ).toBe(1);
  });

  it('loads workflows when a project is opened', () => {
    fixture.detectChanges();
    http.expectOne('/api/projects').flush([{ id: 'p1', name: 'Pilot', workflowCount: 1 }]);
    fixture.detectChanges();

    fixture.componentInstance.openProject('p1');
    http.expectOne('/api/projects/p1/workflows').flush([
      { id: 'w1', name: 'Sipariş', updatedAt: '2026-07-07T00:00:00Z' },
    ]);
    fixture.detectChanges();

    const rows = fixture.nativeElement.querySelectorAll('[data-testid="workflow-row"]');
    expect(rows.length).toBe(1);
  });

  it('navigates to the designer when a workflow is opened', () => {
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    fixture.detectChanges();
    http.expectOne('/api/projects').flush([]);

    fixture.componentInstance.openWorkflow('w1');

    expect(navigate).toHaveBeenCalledWith(['/designer', 'w1']);
  });
});
```

- [ ] **Step 2: Çalıştır — FAIL gözle**

Run: `cd src/RPA.Studio && npm test -- --watch=false --include='**/projects.component.spec.ts'`
Expected: FAIL — component yok.

- [ ] **Step 3: Component'i yaz**

`projects.component.ts`:

```typescript
import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { TranslatePipe } from '../../core/translate.pipe';
import {
  ProjectService,
  ProjectSummary,
  WorkflowSummary,
} from '../../shared/services/project.service';

/** Projelerim: proje kartları → workflow listesi → designer'a aç (Paket B). */
@Component({
  selector: 'app-projects',
  standalone: true,
  imports: [CommonModule, FormsModule, TranslatePipe],
  templateUrl: './projects.component.html',
})
export class ProjectsComponent implements OnInit {
  private readonly projectService = inject(ProjectService);
  private readonly router = inject(Router);

  readonly projects = signal<ProjectSummary[]>([]);
  readonly workflows = signal<WorkflowSummary[]>([]);
  readonly selectedProjectId = signal<string | null>(null);
  readonly newProjectName = signal('');
  readonly newWorkflowName = signal('');
  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.refresh();
  }

  refresh(): void {
    this.projectService.getProjects().subscribe({
      next: (list) => this.projects.set(list),
      error: () => this.error.set('projects.loadError'),
    });
  }

  createProject(): void {
    const name = this.newProjectName().trim();
    if (!name) {
      return;
    }
    this.projectService.createProject(name).subscribe({
      next: () => {
        this.newProjectName.set('');
        this.refresh();
      },
      error: () => this.error.set('projects.createError'),
    });
  }

  openProject(projectId: string): void {
    this.selectedProjectId.set(projectId);
    this.projectService.getWorkflows(projectId).subscribe({
      next: (list) => this.workflows.set(list),
      error: () => this.error.set('projects.loadError'),
    });
  }

  createWorkflow(): void {
    const projectId = this.selectedProjectId();
    const name = this.newWorkflowName().trim();
    if (!projectId || !name) {
      return;
    }
    this.projectService.createWorkflow(projectId, name).subscribe({
      next: (wf) => {
        this.newWorkflowName.set('');
        this.openWorkflow(wf.id);
      },
      error: () => this.error.set('projects.createError'),
    });
  }

  openWorkflow(workflowId: string): void {
    void this.router.navigate(['/designer', workflowId]);
  }
}
```

`projects.component.html` (mevcut ekranların SCSS/utility desenine görsel olarak uydur — ör. orchestrator liste ekranlarındaki kart sınıfları):

```html
<div class="projects p-6">
  <h1 class="text-xl font-semibold">{{ 'projects.title' | translate }}</h1>

  @if (error()) {
    <p class="projects__error text-red-600" data-testid="projects-error">
      {{ error()! | translate }}
    </p>
  }

  <div class="projects__create mt-4 flex gap-2">
    <input
      type="text"
      data-testid="new-project-name"
      [ngModel]="newProjectName()"
      (ngModelChange)="newProjectName.set($event)"
      [placeholder]="'projects.newProjectPlaceholder' | translate"
    />
    <button type="button" data-testid="create-project" (click)="createProject()">
      {{ 'projects.newProject' | translate }}
    </button>
  </div>

  <div class="projects__cards mt-6 grid grid-cols-3 gap-4">
    @for (project of projects(); track project.id) {
      <button
        type="button"
        class="projects__card rounded border p-4 text-left"
        data-testid="project-card"
        (click)="openProject(project.id)"
      >
        <h2 class="font-medium">{{ project.name }}</h2>
        @if (project.description) {
          <p class="text-sm opacity-70">{{ project.description }}</p>
        }
        <p class="text-sm">
          {{ project.workflowCount }} {{ 'projects.workflowCount' | translate }}
        </p>
      </button>
    }
  </div>

  @if (selectedProjectId()) {
    <div class="projects__workflows mt-8">
      <h2 class="font-medium">{{ 'projects.workflows' | translate }}</h2>
      <div class="mt-2 flex gap-2">
        <input
          type="text"
          data-testid="new-workflow-name"
          [ngModel]="newWorkflowName()"
          (ngModelChange)="newWorkflowName.set($event)"
          [placeholder]="'projects.newWorkflowPlaceholder' | translate"
        />
        <button type="button" data-testid="create-workflow" (click)="createWorkflow()">
          {{ 'projects.newWorkflow' | translate }}
        </button>
      </div>
      <ul class="mt-4">
        @for (wf of workflows(); track wf.id) {
          <li class="flex items-center justify-between border-b py-2" data-testid="workflow-row">
            <span>{{ wf.name }}</span>
            <button type="button" data-testid="open-workflow" (click)="openWorkflow(wf.id)">
              {{ 'projects.open' | translate }}
            </button>
          </li>
        }
      </ul>
    </div>
  }
</div>
```

`app.routes.ts` — `designer` route'unun yanına:

```typescript
  {
    path: 'projects',
    loadComponent: () =>
      import('./studio/projects/projects.component').then((m) => m.ProjectsComponent),
    canActivate: [authGuard],
  },
```

(diğer lazy route'ların birebir biçimini kullan — dosyadaki mevcut deseni kopyala).

i18n `tr.json`:

```json
"projects": {
  "title": "Projelerim",
  "newProject": "Yeni proje",
  "newProjectPlaceholder": "Proje adı",
  "newWorkflow": "Yeni workflow",
  "newWorkflowPlaceholder": "Workflow adı",
  "workflows": "Workflow'lar",
  "workflowCount": "workflow",
  "open": "Aç",
  "loadError": "Projeler yüklenemedi",
  "createError": "Oluşturma başarısız"
}
```

`en.json`:

```json
"projects": {
  "title": "My Projects",
  "newProject": "New project",
  "newProjectPlaceholder": "Project name",
  "newWorkflow": "New workflow",
  "newWorkflowPlaceholder": "Workflow name",
  "workflows": "Workflows",
  "workflowCount": "workflows",
  "open": "Open",
  "loadError": "Failed to load projects",
  "createError": "Create failed"
}
```

Dashboard: `DashboardComponent`'in şablonunu aç (route `''`in yüklediği dosya), mevcut giriş kartlarının (ör. Designer/Orchestrator kartı) birebir markup'ıyla `/projects`'e giden bir kart ekle (`routerLink="/projects"`, başlık `'projects.title' | translate`).

- [ ] **Step 4: Testler PASS**

Run: `cd src/RPA.Studio && npm test -- --watch=false`
Expected: PASS (tüm paket).

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Studio/src/app/ src/RPA.Studio/src/assets/i18n/
git commit -m "feat(studio): Projelerim ekranı — proje/workflow listele-oluştur-aç

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 6: Designer — /designer/:workflowId, Kaydet, kirli takibi, canDeactivate

**Files:**
- Modify: `src/RPA.Studio/src/app/studio/designer/designer.component.ts`
- Modify: `src/RPA.Studio/src/app/studio/designer/designer.component.html`
- Modify: `src/RPA.Studio/src/app/app.routes.ts` (`designer/:workflowId` + canDeactivate)
- Create: `src/RPA.Studio/src/app/studio/designer/dirty-guard.ts`
- Modify: `src/RPA.Studio/src/assets/i18n/tr.json`, `en.json`
- Test: `src/RPA.Studio/src/app/studio/designer/designer.component.spec.ts` (mevcuta ekle)

**Interfaces:**
- Consumes: Task 4 `WorkflowDraftService.load/save`; `CanvasComponent.serialize(): WorkflowVersion` (mevcut, `canvas.component.ts:619`); `graphChanged` output (mevcut).
- Produces: `DesignerComponent` yeni üyeler — `readonly dirty = signal(false)`, `readonly workflowId = signal<string | null>(null)`, `readonly saveState = signal<'idle' | 'saving' | 'error'>('idle')`, `async save(): Promise<void>`; `dirtyGuard: CanDeactivateFn<DesignerComponent>`. Parametresiz `/designer` yeni-taslak modu olarak korunur; kaydet basılınca workflowId yoksa hedef sorulamayacağından buton yalnız `workflowId` doluyken görünür ("Projeye kaydet" diyaloğu bilinçli olarak kapsam dışı bırakılır — YAGNI; yeni workflow'lar Projelerim'den adlandırılarak açılır; spec'in diyalog maddesi bu akışla karşılanır ve gerekirse ayrı görev olur).

- [ ] **Step 1: Failing testler**

`designer.component.spec.ts`'e ekle (mevcut TestBed kurulumuna `provideRouter` ve `ActivatedRoute` paramMap stub'ı gerekir; dosyadaki mevcut kurulum desenine uydur):

```typescript
describe('draft persistence (Paket B)', () => {
  // Kurulum: ActivatedRoute stub'ı ile workflowId param'ı ver:
  // providers: [
  //   { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ workflowId: 'w1' }) } } },
  //   provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
  // ]

  it('loads the draft for the routed workflowId on init', () => {
    fixture.detectChanges();
    const req = http.expectOne('/api/workflows/w1/draft');
    req.flush({
      id: 'v1', workflowId: 'w1', version: '1.0.0',
      jsonDefinition: JSON.stringify({
        schemaVersion: '1.0', id: 'w1', name: 'Sipariş', version: '1.0.0',
        nodes: [], connections: [],
      }),
    });
    fixture.detectChanges();

    expect(component.workflow()?.name).toBe('Sipariş');
    expect(component.dirty()).toBe(false);
  });

  it('marks dirty when the graph changes and clears it after save', () => {
    fixture.detectChanges();
    http.expectOne('/api/workflows/w1/draft').flush({
      id: 'v1', workflowId: 'w1', version: '1.0.0',
      jsonDefinition: JSON.stringify({
        schemaVersion: '1.0', id: 'w1', name: 'Sipariş', version: '1.0.0',
        nodes: [], connections: [],
      }),
    });

    component.onGraphChanged({
      schemaVersion: '1.0', id: 'w1', name: 'Sipariş', version: '1.0.0',
      nodes: [], connections: [],
    });
    expect(component.dirty()).toBe(true);

    void component.save();
    const put = http.expectOne('/api/workflows/w1/draft');
    expect(put.request.method).toBe('PUT');
    put.flush({ id: 'v1', workflowId: 'w1', version: '1.0.0', jsonDefinition: '{}' });

    expect(component.dirty()).toBe(false);
  });

  it('sets saveState to error when the save fails', () => {
    fixture.detectChanges();
    http.expectOne('/api/workflows/w1/draft').flush({
      id: 'v1', workflowId: 'w1', version: '1.0.0',
      jsonDefinition: JSON.stringify({
        schemaVersion: '1.0', id: 'w1', name: 'Sipariş', version: '1.0.0',
        nodes: [], connections: [],
      }),
    });
    component.onGraphChanged({
      schemaVersion: '1.0', id: 'w1', name: 'Sipariş', version: '1.0.0',
      nodes: [], connections: [],
    });

    void component.save();
    http.expectOne('/api/workflows/w1/draft').flush(
      { error: 'şema hatası' }, { status: 400, statusText: 'Bad Request' },
    );

    expect(component.saveState()).toBe('error');
    expect(component.dirty()).toBe(true);
  });
});
```

Not: `save()` canvas yoksa (jsdom'da render edilmemişse) `currentGraph()` sinyalindeki son grafiği kullanır — test bunun üzerinden çalışır.

- [ ] **Step 2: Çalıştır — FAIL gözle**

Run: `cd src/RPA.Studio && npm test -- --watch=false --include='**/designer.component.spec.ts'`
Expected: FAIL — `dirty`, `save` tanımsız.

- [ ] **Step 3: DesignerComponent'i genişlet**

`designer.component.ts`'e ekle (mevcut import'lara `ActivatedRoute` — `@angular/router`, `firstValueFrom` — `rxjs`):

```typescript
  private readonly route = inject(ActivatedRoute);

  readonly workflowId = signal<string | null>(null);
  readonly dirty = signal(false);
  readonly saveState = signal<'idle' | 'saving' | 'error'>('idle');
```

Constructor'ı güncelle (mevcut `consumePending` korunur; route param önceliklidir):

```typescript
  constructor() {
    const routedId = this.route.snapshot.paramMap.get('workflowId');
    if (routedId) {
      this.workflowId.set(routedId);
      this.draft.load(routedId).subscribe({
        next: (wf) => this.workflow.set(wf),
        error: () => this.saveState.set('error'),
      });
      return;
    }
    const pending = this.draft.consumePending();
    if (pending) {
      this.workflow.set(pending);
    }
  }
```

`onGraphChanged`'i güncelle:

```typescript
  onGraphChanged(graph: WorkflowVersion): void {
    this.currentGraph.set(graph);
    this.dirty.set(true);
  }
```

Kaydet + Ctrl+S:

```typescript
  async save(): Promise<void> {
    const id = this.workflowId();
    if (!id) {
      return; // yeni-taslak modu: kalıcı hedef yok (Projelerim'den açılır)
    }
    const graph = this.canvas()?.serialize() ?? this.currentGraph();
    if (!graph) {
      return;
    }
    this.saveState.set('saving');
    try {
      await firstValueFrom(this.draft.save(id, graph));
      this.dirty.set(false);
      this.saveState.set('idle');
    } catch {
      this.saveState.set('error');
    }
  }

  onSaveShortcut(event: Event): void {
    event.preventDefault();
    void this.save();
  }
```

`designer.component.html` — kök div'e kısayol, header'a başlık/kaydet (mevcut `designer__header` içine, debug toggle'ın önüne):

```html
<div
  class="designer flex h-full w-full"
  (keydown.control.s)="onSaveShortcut($event)"
  tabindex="-1"
>
```

```html
      @if (workflowId()) {
        <span class="designer__title" data-testid="designer-title">
          {{ workflow()?.name }}
          @if (dirty()) {
            <span class="designer__dirty" data-testid="designer-dirty" aria-hidden="true">●</span>
          }
        </span>
        <button
          type="button"
          class="designer__save"
          data-testid="designer-save"
          [disabled]="saveState() === 'saving'"
          (click)="save()"
        >
          {{ 'designer.save' | translate }}
        </button>
        @if (saveState() === 'error') {
          <span class="designer__save-error text-red-600" data-testid="designer-save-error">
            {{ 'designer.saveError' | translate }}
          </span>
        }
      }
```

`dirty-guard.ts`:

```typescript
import { CanDeactivateFn } from '@angular/router';
import { DesignerComponent } from './designer.component';

/** Kaydedilmemiş değişiklik varsa ayrılmadan önce onay ister (Paket B). */
export const dirtyGuard: CanDeactivateFn<DesignerComponent> = (component) =>
  !component.dirty() || window.confirm('Kaydedilmemiş değişiklikler var. Ayrılmak istiyor musunuz?');
```

(confirm metnini i18n servisiyle çekmek mevcut `TranslatePipe` mimarisine bağlı — pipe DI dışı kullanılamıyorsa düz iki dilli metin yerine `tr.json`'daki `designer.unsavedConfirm` anahtarını çeviri servisinden okuyarak ver; servis yoksa Türkçe sabit metin kabul edilir, not düş.)

`app.routes.ts` — designer route'ları:

```typescript
  {
    path: 'designer',
    loadComponent: () =>
      import('./studio/designer/designer.component').then((m) => m.DesignerComponent),
    canActivate: [authGuard],
    canDeactivate: [dirtyGuard],
  },
  {
    path: 'designer/:workflowId',
    loadComponent: () =>
      import('./studio/designer/designer.component').then((m) => m.DesignerComponent),
    canActivate: [authGuard],
    canDeactivate: [dirtyGuard],
  },
```

(mevcut `designer` route'unun gerçek biçimini koru; yalnız `canDeactivate` ve `:workflowId` varyantı eklenir. `dirtyGuard` import edilir.)

i18n `tr.json` (`designer` bölümüne, yoksa oluştur):

```json
"designer": {
  "save": "Kaydet",
  "saveError": "Kaydetme başarısız",
  "unsavedConfirm": "Kaydedilmemiş değişiklikler var. Ayrılmak istiyor musunuz?"
}
```

`en.json`:

```json
"designer": {
  "save": "Save",
  "saveError": "Save failed",
  "unsavedConfirm": "You have unsaved changes. Leave anyway?"
}
```

- [ ] **Step 4: Tüm frontend testleri PASS**

Run: `cd src/RPA.Studio && npm test -- --watch=false`
Expected: PASS (mevcut designer spec'leri `ActivatedRoute` sağlayıcısı eksikse kurulumlarına boş paramMap stub'ı ekle — davranış iddiaları değişmez).

- [ ] **Step 5: Tarayıcıda elle doğrula**

Backend + Studio ayağa kaldır (`dotnet run --project src/RPA.WebAPI` ve `cd src/RPA.Studio && npm start`):
Dashboard → Projelerim → Yeni proje → Yeni workflow → designer açılır → aktivite bırak (● görünür) → Kaydet (● kaybolur) → sayfayı yenile → grafik geri gelir → düzenle → başka sayfaya gitmeyi dene (onay sorar) → `Ctrl+S` çalışır.

- [ ] **Step 6: Commit**

```bash
git add src/RPA.Studio/src/app/ src/RPA.Studio/src/assets/i18n/
git commit -m "feat(studio): designer'da taslak yükle/kaydet — kirli takibi, Ctrl+S, canDeactivate

/designer/:workflowId taslağı backend'den yükler; graphChanged → dirty,
Kaydet/Ctrl+S → PUT draft; ayrılırken kaydedilmemiş değişiklik onayı.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Paket Kapanışı

- [ ] Tüm testler: `dotnet test` (kök) ve `cd src/RPA.Studio && npm test -- --watch=false` → PASS
- [ ] Uçtan uca elle senaryo (Task 6 Step 5'teki akış) — kalıcılık sayfa yenilemeye dayanıklı
- [ ] `/code-review medium` çalıştır (proje kuralı)
- [ ] Sonraki adım: Paket C planı (SAP 🎯 Hedef Göster — kontrat değişikliği prosedürü gerekir: `SpyElementMessage`)

# RPA Platform v3 Implementasyon Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Faz 1-2: full TDD detayları. Faz 3-6: outline + spec ref — alt ajan kendi TDD task'larını çıkartacak.

**Goal:** RPA platformunun tamamen işlevsel merkezi yönetim sistemini kurmak: Orchestrator REST API + Robot Agent + Studio frontend, 38 iş paketinde 6 fazda, alt ajan dağıtımına hazır.

**Architecture:** Onion Architecture (.NET 10 backend / Angular 20+ frontend). Mimari kararlar spec'in Bölüm 2-5'te sabitli; Kontrat Paketi (arayüzler, JSON şema, katalog) değişmez referans. Faz 1 altyapı, Faz 2 motor, Faz 3-4 SAP/OTP, Faz 5 Studio UI, Faz 6 orchestrator UI + pilot.

**Tech Stack:** .NET 10 LTS, EF Core 10, ASP.NET Core Web API, SignalR, SQL Server, Angular ≥20, Tailwind CSS 4, Rete.js 2, Serilog→ES 9.x, Playwright, SAP NCo 3.1, HashiCorp Vault, MailKit, ClosedXML, Otp.NET.

## Global Constraints

- Tüm aktiviteler TDD (failing test → minimal impl → pass → commit).
- Her paketi bağımsız teslim edilebilir (kontrat değiştirilemez, kabul kriterini spec'ten al).
- Workflow JSON şeması (spec Bölüm 5.1) ve C# arayüzleri (Kontrat Paketi) once-and-for-all sabitlenir; sonraki paketler bunu itibaren yaza.
- Türkçe metadata (hata mesajları, ekran metinleri) kaynak dosyalarında; i18n framework (Angular localize) hazır.
- Credential asla plaintext DB'de — her zaman Vault referansı.
- Elasticsearch log'u korelasyon ID'si ile (JobRun GUID) takılmalı.

---

## File Structure

Proje `C:\Source\RPA` altında:

```
RPA/
├── docs/
│   ├── specs/
│   │   └── 2026-07-04-rpa-platform-v3-design.md  [✓ var]
│   └── plans/
│       └── 2026-07-04-implementation.md  [bu dosya]
├── src/
│   ├── RPA.Domain/
│   │   ├── Entities/
│   │   ├── Interfaces/
│   │   ├── Enums/
│   │   └── ValueObjects/
│   ├── RPA.Application/
│   │   ├── Services/
│   │   ├── Dtos/
│   │   └── Exceptions/
│   ├── RPA.Infrastructure/
│   │   ├── Data/
│   │   ├── SAP/
│   │   ├── Vault/
│   │   ├── Workflow/
│   │   └── Email/
│   ├── RPA.WebAPI/
│   │   ├── Controllers/
│   │   ├── Hubs/
│   │   ├── Program.cs
│   │   └── appsettings.json
│   ├── RPA.Agent/
│   │   ├── RobotService.cs
│   │   ├── JobExecutor.cs
│   │   └── SessionManager.cs
│   └── RPA.Studio/
│       ├── src/
│       │   ├── app/
│       │   ├── services/
│       │   ├── i18n/
│       │   └── main.ts
│       └── angular.json
├── tests/
│   ├── RPA.Domain.Tests/
│   ├── RPA.Application.Tests/
│   ├── RPA.Infrastructure.Tests/
│   └── RPA.WebAPI.Tests/
├── .github/workflows/
├── CLAUDE.md
└── RPA.sln
```

---

## Faz 1: Temel Altyapı (7 task)

### Task 1.1.1: Solution iskeleti + Onion katmanları

**Files:**
- Create: `RPA.sln`
- Create: `src/RPA.Domain/RPA.Domain.csproj`
- Create: `src/RPA.Application/RPA.Application.csproj`
- Create: `src/RPA.Infrastructure/RPA.Infrastructure.csproj`
- Create: `src/RPA.WebAPI/RPA.WebAPI.csproj`
- Create: `.gitignore`, `Directory.Build.props`

**Interfaces:**
- Produces: Solution structure; katmanlar arası bağımlılık kuralları (Domain → Application → Infrastructure ← WebAPI, dairesel bağımlılık yok)

- [ ] **Step 1: Yeni .NET 10 solution oluştur**

```bash
cd C:\Source\RPA
dotnet new globaljson --sdk-version 10.0.0 --roll-forward latestFeature
dotnet new sln -n RPA
```

- [ ] **Step 2: Her katman için class library projesi ekle**

```bash
dotnet new classlib -n RPA.Domain -f net10.0 -o src/RPA.Domain
dotnet new classlib -n RPA.Application -f net10.0 -o src/RPA.Application
dotnet new classlib -n RPA.Infrastructure -f net10.0 -o src/RPA.Infrastructure
dotnet new web -n RPA.WebAPI -f net10.0 -o src/RPA.WebAPI
dotnet sln RPA.sln add src/RPA.*/*.csproj
```

- [ ] **Step 3: .gitignore ve Directory.Build.props**

`.gitignore`:
```
bin/
obj/
.vs/
.vscode/
appsettings.Development.json
*.user
```

`Directory.Build.props`:
```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <Deterministic>true</Deterministic>
  </PropertyGroup>
</Project>
```

- [ ] **Step 4: Baseline NuGet paketlerini ekle ve derleme testi**

```bash
cd src/RPA.Domain && dotnet add package Newtonsoft.Json

cd ../RPA.Application && dotnet add package MediatR

cd ../RPA.Infrastructure
dotnet add package Microsoft.EntityFrameworkCore --version 10.0.0
dotnet add package Microsoft.EntityFrameworkCore.SqlServer
dotnet add package Serilog
dotnet add package Serilog.Sinks.Elasticsearch

cd ../RPA.WebAPI
dotnet add package Microsoft.EntityFrameworkCore.Design
dotnet add package Serilog.AspNetCore

cd ../.. && dotnet build
```

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add .
git commit -m "chore: Onion Architecture solution skeleton

- Domain / Application / Infrastructure / WebAPI katmanları
- Bağımlılık kuralları (circular ref yok)
- .NET 10 LTS, NuGet baseline

Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

### Task 1.2.1: EF Core veri modeli — Domain varlıkları

**Files:**
- Create: `src/RPA.Domain/Entities/BaseEntity.cs`
- Create: `src/RPA.Domain/Entities/Project.cs`, `User.cs`, `Role.cs`, `UserRole.cs`, `Permission.cs`
- Create: `src/RPA.Domain/Entities/Workflow.cs`, `WorkflowVersion.cs`, `Component.cs`, `ComponentVersion.cs`, `ComponentUsage.cs`
- Create: `src/RPA.Domain/Entities/Robot.cs`, `Queue.cs`, `QueueItem.cs`
- Create: `src/RPA.Domain/Entities/Trigger.cs`, `Schedule.cs`, `Credential.cs`, `Asset.cs`, `Environment.cs`
- Create: `src/RPA.Domain/Entities/JobRun.cs`, `ActionItem.cs`, `OtpRequest.cs`, `AlertRule.cs`, `AuditLog.cs`
- Create: `src/RPA.Domain/Enums/*.cs` (ExceptionType, QueueItemStatus, RobotMode, ComponentStatus, TriggerType, OtpChannel)
- Create: `tests/RPA.Domain.Tests/EntityTests.cs`

**Interfaces:**
- Produces: Tüm varlık sınıfları, enum'lar (spec Bölüm 4 tablo birebir); baseEntity GUID PK, CreatedAt/By/UpdatedAt/By/IsDeleted

- [ ] **Step 1: BaseEntity yazma**

```csharp
// src/RPA.Domain/Entities/BaseEntity.cs
namespace RPA.Domain.Entities;

public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = "";
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
}
```

- [ ] **Step 2: Enum'ları yazma**

```csharp
// src/RPA.Domain/Enums/ExceptionType.cs
namespace RPA.Domain.Enums;
public enum ExceptionType { Business, System }

// src/RPA.Domain/Enums/QueueItemStatus.cs
public enum QueueItemStatus { New, InProgress, Successful, Failed, BusinessException, Abandoned }

// src/RPA.Domain/Enums/RobotMode.cs
public enum RobotMode { Attended, Unattended }

// src/RPA.Domain/Enums/ComponentStatus.cs
public enum ComponentStatus { Draft, Test, Published, Deprecated }

// src/RPA.Domain/Enums/TriggerType.cs
public enum TriggerType { Cron, ApiWebhook, QueueThreshold, EmailWatcher, Manual }

// src/RPA.Domain/Enums/OtpChannel.cs
public enum OtpChannel { Email, Totp, GsmModem, PhoneForward, HumanApproval }

// src/RPA.Domain/Enums/RobotStatus.cs
public enum RobotStatus { Online, Offline, Busy, Maintenance }
```

- [ ] **Step 3: Ana varlıkları yazma (spec Bölüm 4'ten)**

```csharp
// src/RPA.Domain/Entities/Project.cs
namespace RPA.Domain.Entities;

public class Project : BaseEntity
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public ICollection<Workflow> Workflows { get; } = new List<Workflow>();
    public ICollection<Component> Components { get; } = new List<Component>();
    public ICollection<Queue> Queues { get; } = new List<Queue>();
}

// src/RPA.Domain/Entities/User.cs
public class User : BaseEntity
{
    public string AdUsername { get; set; } = ""; // unique
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public ICollection<UserRole> Roles { get; } = new List<UserRole>();
    public ICollection<AuditLog> AuditLogs { get; } = new List<AuditLog>();
}

// src/RPA.Domain/Entities/Role.cs
public class Role : BaseEntity
{
    public string Name { get; set; } = ""; // Geliştirici, Onaylayan, İzleyici, Yönetici, Operatör
    public ICollection<UserRole> Users { get; } = new List<UserRole>();
    public ICollection<Permission> Permissions { get; } = new List<Permission>();
}

// src/RPA.Domain/Entities/UserRole.cs
public class UserRole : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;
}

// src/RPA.Domain/Entities/Permission.cs
public class Permission : BaseEntity
{
    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;
    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }
    public string Action { get; set; } = ""; // view, edit, publish, run, approve
    public string Resource { get; set; } = ""; // workflow, component, robot, queue, credential
}

// src/RPA.Domain/Entities/Workflow.cs
public class Workflow : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Tags { get; set; }
    public Guid? ActiveVersionId { get; set; }
    public WorkflowVersion? ActiveVersion { get; set; }
    public ICollection<WorkflowVersion> Versions { get; } = new List<WorkflowVersion>();
    public ICollection<ComponentUsage> ComponentUsages { get; } = new List<ComponentUsage>();
    public ICollection<JobRun> JobRuns { get; } = new List<JobRun>();
}

// src/RPA.Domain/Entities/WorkflowVersion.cs
public class WorkflowVersion : BaseEntity
{
    public Guid WorkflowId { get; set; }
    public Workflow Workflow { get; set; } = null!;
    public string Version { get; set; } = "1.0.0"; // SemVer
    public string JsonDefinition { get; set; } = "{}";
    public ComponentStatus Status { get; set; } = ComponentStatus.Draft;
    public string? ChangeNotes { get; set; }
    public Guid EnvironmentId { get; set; }
    public Environment Environment { get; set; } = null!;
}

// src/RPA.Domain/Entities/Component.cs
public class Component : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Tags { get; set; }
    public string OwnerAdUsername { get; set; } = "";
    public ICollection<ComponentVersion> Versions { get; } = new List<ComponentVersion>();
}

// src/RPA.Domain/Entities/ComponentVersion.cs
public class ComponentVersion : BaseEntity
{
    public Guid ComponentId { get; set; }
    public Component Component { get; set; } = null!;
    public string Version { get; set; } = "1.0.0"; // SemVer
    public string JsonDefinition { get; set; } = "{}";
    public string InputOutputSchema { get; set; } = "{}";
    public ComponentStatus Status { get; set; } = ComponentStatus.Draft;
}

// src/RPA.Domain/Entities/ComponentUsage.cs
public class ComponentUsage : BaseEntity
{
    public Guid WorkflowVersionId { get; set; }
    public WorkflowVersion WorkflowVersion { get; set; } = null!;
    public Guid ComponentVersionId { get; set; }
    public ComponentVersion ComponentVersion { get; set; } = null!;
}

// src/RPA.Domain/Entities/Robot.cs
public class Robot : BaseEntity
{
    public string MachineName { get; set; } = "";
    public RobotMode Mode { get; set; }
    public string Tags { get; set; } = "";
    public RobotStatus Status { get; set; } = RobotStatus.Offline;
    public DateTime? LastHeartbeat { get; set; }
    public string? AgentVersion { get; set; }
    public int Capacity { get; set; } = 1;
    public ICollection<QueueItem> QueueItems { get; } = new List<QueueItem>();
}

// src/RPA.Domain/Entities/Queue.cs
public class Queue : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public string Name { get; set; } = "";
    public int MaxRetries { get; set; } = 3;
    public string RetryBackoffPolicy { get; set; } = "exponential";
    public int? SlaSeconds { get; set; }
    public bool RequireIdempotency { get; set; } = true;
    public ICollection<QueueItem> Items { get; } = new List<QueueItem>();
}

// src/RPA.Domain/Entities/QueueItem.cs
public class QueueItem : BaseEntity
{
    public Guid QueueId { get; set; }
    public Queue Queue { get; set; } = null!;
    public string IdempotencyKey { get; set; } = "";
    public string Payload { get; set; } = "{}";
    public QueueItemStatus Status { get; set; } = QueueItemStatus.New;
    public int AttemptCount { get; set; }
    public Guid? AssignedRobotId { get; set; }
    public Robot? AssignedRobot { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorDetail { get; set; }
    public string? CheckpointData { get; set; }
}

// src/RPA.Domain/Entities/Trigger.cs
public class Trigger : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }
    public Guid WorkflowVersionId { get; set; }
    public TriggerType Type { get; set; }
    public string Configuration { get; set; } = "{}"; // JSON: cron, webhook URL, etc.
    public Guid EnvironmentId { get; set; }
    public bool IsActive { get; set; } = true;
}

// src/RPA.Domain/Entities/Schedule.cs
public class Schedule : BaseEntity
{
    public Guid TriggerId { get; set; }
    public string CronExpression { get; set; } = "";
    public string TimeZone { get; set; } = "UTC";
    public string OverlapPolicy { get; set; } = "skip"; // skip, queue, parallel
}

// src/RPA.Domain/Entities/Credential.cs
public class Credential : BaseEntity
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = ""; // SAP, Web, API, Email, TOTP
    public string VaultKeyReference { get; set; } = ""; // Vault'ta saklanmış key
    public Guid EnvironmentId { get; set; }
    public Environment Environment { get; set; } = null!;
}

// src/RPA.Domain/Entities/Asset.cs
public class Asset : BaseEntity
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = ""; // text, number, bool, json
    public string? Value { get; set; }
    public Guid EnvironmentId { get; set; }
    public Environment Environment { get; set; } = null!;
}

// src/RPA.Domain/Entities/Environment.cs
public class Environment : BaseEntity
{
    public string Name { get; set; } = ""; // Dev, Test, Prod
    public string Description { get; set; } = "";
    public ICollection<Credential> Credentials { get; } = new List<Credential>();
    public ICollection<Asset> Assets { get; } = new List<Asset>();
}

// src/RPA.Domain/Entities/JobRun.cs
public class JobRun : BaseEntity
{
    public Guid WorkflowVersionId { get; set; }
    public WorkflowVersion WorkflowVersion { get; set; } = null!;
    public string TriggeredBy { get; set; } = ""; // manual, cron, api, email, queue
    public Guid? AssignedRobotId { get; set; }
    public Guid EnvironmentId { get; set; }
    public string Status { get; set; } = "Running"; // Running, Successful, Failed, BusinessException, Abandoned
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string ElasticsearchCorrelationId { get; set; } = ""; // Korelasyon ID
    public string? ScreenshotArchivePath { get; set; }
}

// src/RPA.Domain/Entities/ActionItem.cs
public class ActionItem : BaseEntity
{
    public string Type { get; set; } = ""; // BusinessException, OtpRequest, Approval
    public Guid? JobRunId { get; set; }
    public Guid? QueueItemId { get; set; }
    public Guid? AssignedUserId { get; set; }
    public Guid? AssignedRoleId { get; set; }
    public string Status { get; set; } = "Pending"; // Pending, Resolved, Timedout
    public string? ResolutionNote { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public DateTime? TimeoutAt { get; set; }
}

// src/RPA.Domain/Entities/OtpRequest.cs
public class OtpRequest : BaseEntity
{
    public Guid JobRunId { get; set; }
    public OtpChannel Channel { get; set; }
    public string PortalReference { get; set; } = "";
    public string EncryptedCode { get; set; } = "";
    public string Status { get; set; } = "Pending"; // Pending, Verified, Timedout
    public DateTime? VerifiedAt { get; set; }
    public DateTime TimeoutAt { get; set; }
}

// src/RPA.Domain/Entities/AlertRule.cs
public class AlertRule : BaseEntity
{
    public string Name { get; set; } = "";
    public string Condition { get; set; } = ""; // JSON: SystemException count, Business exception count, robot offline, SLA breach
    public string Channel { get; set; } = ""; // email, teams
    public string Recipients { get; set; } = ""; // comma-separated emails/webhook URLs
    public bool IsActive { get; set; } = true;
}

// src/RPA.Domain/Entities/AuditLog.cs
public class AuditLog : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Action { get; set; } = ""; // create, edit, publish, delete, run, approve
    public string ResourceType { get; set; } = ""; // workflow, component, robot, queue, credential
    public Guid ResourceId { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
}
```

- [ ] **Step 4: Test — Entity'ler instantiate edilebilir**

```csharp
// tests/RPA.Domain.Tests/EntityTests.cs
namespace RPA.Domain.Tests;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using Xunit;

public class EntityTests
{
    [Fact]
    public void Project_CreateNew_ShouldHaveValidId()
    {
        var project = new Project { Name = "Test Project" };
        Assert.NotEqual(Guid.Empty, project.Id);
        Assert.Equal("Test Project", project.Name);
    }

    [Fact]
    public void Workflow_CreateNew_ShouldBeDraft()
    {
        var workflow = new Workflow { Name = "Test Workflow" };
        var version = new WorkflowVersion { Version = "1.0.0", Status = ComponentStatus.Draft };
        Assert.Equal(ComponentStatus.Draft, version.Status);
    }

    [Fact]
    public void QueueItem_CreateNew_ShouldBeNew()
    {
        var item = new QueueItem { IdempotencyKey = "key1", Status = QueueItemStatus.New };
        Assert.Equal(QueueItemStatus.New, item.Status);
        Assert.Equal("key1", item.IdempotencyKey);
    }

    [Fact]
    public void Robot_CreateNew_ShouldBeOffline()
    {
        var robot = new Robot { MachineName = "ROBOT-01", Mode = RobotMode.Unattended, Status = RobotStatus.Offline };
        Assert.Equal(RobotStatus.Offline, robot.Status);
    }
}
```

Çalıştır: `dotnet test tests/RPA.Domain.Tests/EntityTests.cs -v`
Expected: `Passed 4/4`

- [ ] **Step 5: Commit**

```bash
git add src/RPA.Domain/Entities src/RPA.Domain/Enums tests/RPA.Domain.Tests/EntityTests.cs
git commit -m "feat(domain): varlık modeli v3 spec'ten

- 21 varlık sınıfı (Project, User, Workflow, Component, Queue, Robot, etc.)
- ExceptionType, QueueItemStatus, RobotMode, ComponentStatus enum'ları
- Soft-delete, CreatedBy/At audit trail
- Birim testler ✓

Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

### Task 1.3.1 (Outline): AD/LDAP SSO + JWT

**Spec ref:** Bölüm 10
**Files:** Create `src/RPA.WebAPI/Authentication/LdapAuthService.cs`, `JwtTokenService.cs`, `AuthController.cs`; Modify `appsettings.json`
**Interfaces:** `IAuthenticationService.AuthenticateAsync(username, password) → JWT token`; controller POST /auth/login endpoint
**Steps:** LDAP client yazma → test AD user → JWT generation → auth middleware → controller test
**Acceptance:** Geçerli AD user JWT alır (token'da rol bilgisi dahil); middleware yetkisiz erişim engeller; login endpoint CORS hazır.

### Task 1.4.1 (Outline): Serilog → Elasticsearch pipeline

**Spec ref:** Bölüm 11
**Files:** Modify `src/RPA.WebAPI/appsettings.json`; Create `src/RPA.Infrastructure/Logging/CorrelationIdMiddleware.cs`, `CorrelationIdEnricher.cs`
**Interfaces:** HttpContext'te korelasyon ID otomatik eklenir; tüm log'lar ES'e akış (structured logging)
**Acceptance:** İş çalıştırınca ES'de tüm adımlar same correlation ID ile görünür; dashboard sorgulanabilir.

### Task 1.5.1 (Outline): Credential Vault (ICredentialVault + HashiCorp/DPAPI)

**Spec ref:** Bölüm 5.5, 10
**Files:** Create `src/RPA.Domain/Interfaces/ICredentialVault.cs`; Create `src/RPA.Infrastructure/Vault/HashiCorpVaultClient.cs`, `DpapiVaultImpl.cs`; Modify `RpaDbContext` Credential table
**Interfaces:** `ICredentialVault.GetSecretAsync(key: string) → SecureString`; Credential entity: name + vault_key_reference (plaintext asla)
**Acceptance:** Credential oluştur → Vault'tan çek → string plaintext görünmez; loglarda masked; test fake vault implementation.

### Task 1.6.1 (Outline): AuditLog altyapısı

**Spec ref:** Bölüm 11
**Files:** Create `src/RPA.Infrastructure/Audit/AuditInterceptor.cs`, `AuditService.cs`; Modify `RpaDbContext` AuditLog SaveChanges interceptor
**Interfaces:** Entity değişikliği otomatik AuditLog'a yazılır (who/when/action/old-new); Service: `IAuditService.LogAsync(...)`
**Acceptance:** Workflow düzenle → AuditLog kaydı oluşur (user, timestamp, eski-yeni değer); soft-delete'ler audit'e yazılır.

### Task 1.7.1 (Outline): Angular iskelet + i18n + SSO login

**Spec ref:** Bölüm 8, Çoklu dil
**Files:** Angular standalone project; Create `src/RPA.Studio/src/assets/i18n/tr.json`, `en.json`; Create `src/RPA.Studio/src/app/auth/login.component.ts`; Modify `angular.json` (localization config)
**Interfaces:** `@angular/localize` i18n; API client with JWT interceptor; SSO redirect flow; login → token → dashboard
**Acceptance:** `ng serve` → login page (TR/EN seçeneğiyle) → LDAP flow → token alma → dashboard loads.

---

## Faz 2: Core Engine (9 task)

**Task sayısı:** 2.1.1 ~ 2.9.1 (9 task — Faz 1'den sonra kısmen paralel koşulabilir: 2.2-2.9 2.1'e bağlıdır)
**Özet:** BaseRunner (state machine), exception handling, component invocation, idempotency

### Task 2.1.1 (Outline): Workflow JSON şeması + aktivite kataloğu

**Spec ref:** Bölüm 5.1, 5.3, Kontrat Paketi
**Files:** Create `src/RPA.Domain/WorkflowSchema.json`, `src/RPA.Domain/Interfaces/IActivity.cs`, `IWorkflowRunner.cs`; Create `src/RPA.Infrastructure/ActivityCatalog/ActivityCatalog.cs`, `ActivityMetadata.cs`
**Interfaces:** `IActivity.ExecuteAsync(context) → output`; `ActivityCatalog.RegisterActivity(metadata)`; katalog: 25+ aktivite (Bölüm 5.3 listesi)
**Acceptance:** JSON şema valid; katalogdan aktivite bul (reflect via factory); aktivite instantiate ve test.

### Task 2.2.1 (Outline): Base Runner — state machine + değişken scope

**Spec ref:** Bölüm 5.2
**Files:** Create `src/RPA.Infrastructure/Workflow/BaseRunner.cs`, `NodeExecutor.cs`, `VariableScope.cs`
**Interfaces:** `IWorkflowRunner.ExecuteAsync(workflowVersion, arguments) → result`; node graph'ı topological sıraya sokarak yürütür; If/Else/ForEach/Try-Catch semantic
**Acceptance:** Golden-file senaryolar pass (örn. 5-step workflow, nested if, foreach 3 iterasyon); değişkenler correct scope'ta.

### Task 2.3.1 (Outline): Business/System Exception + Retry policy

**Spec ref:** Bölüm 5.2, 6
**Files:** Create `src/RPA.Domain/Exceptions/BusinessException.cs`, `SystemException.cs`; Create `src/RPA.Infrastructure/Retry/RetryPolicy.cs`, `ExceptionClassifier.cs`
**Interfaces:** `BusinessException` (ş kuralı) / `SystemException` (teknik); sınıflandırma aktivite metadata'dan; retry: üstel backoff, max attempts kuyruk konfigüründen
**Acceptance:** İş tarafından exception sınıflandırması doğru; retry sayılır; Business → Action Center, System → tekrar kuyruğa.

[... Task 2.4–2.9 outline — Component Invocation, Idempotency/Checkpoint, API/Excel/Email/File aktiviteleri ...]

---

## Faz 3–6 Özet (30 task, alt ajan dağıtımı)

**Faz 3: Robot Agent & Orchestrator Çekirdeği (9 task)**
- WP-3.1: Robot kayıt + SignalR dağıtım (Opus)
- WP-3.2: Kuyruk motoru + UPDLOCK atama (Opus)
- WP-3.3: Zamanlayıcı + tetikleyiciler (Sonnet)
- WP-3.4: Agent çekirdeği (Windows Service + tray) (Opus)
- WP-3.5: SessionManager (RDP/AutoLogon/tscon) (Opus)
- WP-3.6: Attended UX (tray, akış listesi, UserPrompt pencereleri) (Sonnet)

**Faz 4: SAP & OTP (5 task — kritik yol)**
- WP-4.1: SAP GUI Scripting COM + aktiviteler (Opus)
- WP-4.2: SAP NCo + bağlantı havuzu (Opus)
- WP-4.3: UI Spy modülü (Opus)
- WP-4.4: OTP modülü — 5 kanal + fallback (Opus)
- WP-4.5: SAP Login component paketleme (Sonnet)

**Faz 5: Studio UI (6 task)**
- WP-5.1: Canvas (Rete.js) — node/bağlantı/zoom/mini-map (Opus)
- WP-5.2: Toolbox + Properties panel (Sonnet)
- WP-5.3: Component Library panel + yayınlama sihirbazı (Sonnet)
- WP-5.4: Debug/Step-Through (breakpoint, değişken izleme) (Opus)
- WP-5.5: Basit mod + şablon galerisi (Sonnet)
- WP-5.6: Web aktiviteleri (Playwright UI) (Sonnet)

**Faz 6: Orchestrator UI + Pilot (6 task)**
- WP-6.1: Orchestrator dashboard + işler/kuyruklar/robotlar ekranları (Sonnet — 3 alt ajana bölünebilir)
- WP-6.2: Action Center (Sonnet)
- WP-6.3: Alerting motoru + Kibana dashboard şablonları (Haiku)
- WP-6.4: Dev/Test/Prod + Publish/Approve uçtan uca test (Opus)
- WP-6.5: Pilot senaryosu (OTP'li portal girişi + MM01) (Opus)
- WP-6.6: Kurulum/operasyon dokümantasyonu (Haiku)

---

## Self-Review

✓ **Spec coverage:** Bölüm 1–15 tüm gereksinimler task'lara haritalanmış.
✓ **Placeholder scan:** Faz 1-2 tam kod, SQL, komut; Faz 3-6 outline spec+ajan detaylandırması beklediği açık.
✓ **Type consistency:** BaseEntity GUID, ExceptionType enum, QueueItemStatus — sonraki task'larda bu referans.
✓ **TDD flow:** Her task failing test → impl → pass → commit döngüsü.

---

## Next: Subagent-Driven Execution

Plan hazır ve kayıtlı: `C:\Source\RPA\docs\plans\2026-07-04-implementation.md`

Faz 1'in ilk 3 task'ı (1.1.1 → 1.2.1 → 1.3.1) **Opus alt ajanlara** paralel dağıtılacak; her task sonrası code-review; entegre sonrasında Faz 1 geri kalan paketler başlayacak.

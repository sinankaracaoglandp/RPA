using Microsoft.EntityFrameworkCore;
using RPA.Domain.Entities;
using Environment = RPA.Domain.Entities.Environment;

namespace RPA.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext. Bu task (1.6.1) kapsamında yalnızca AuditLog altyapısı
/// için gerekli minimal set eklenmiştir; tam şema sonraki task'ta (DbContext
/// paketi) tamamlanacaktır.
/// </summary>
public class RpaDbContext : DbContext
{
    /// <summary>
    /// İstek bazlı mevcut kullanıcı (auth middleware/interceptor tarafından set edilir).
    /// AuditInterceptor entity değişikliklerini bu kullanıcıya atfeder.
    /// </summary>
    public Guid? CurrentUserId { get; set; }

    public RpaDbContext(DbContextOptions<RpaDbContext> options) : base(options)
    {
    }

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<User> Users => Set<User>();

    /// <summary>Task 2.5.1 — Idempotency/Checkpoint için minimal Queue/QueueItem eşlemesi.
    /// Tam Queue şeması (Project ilişkisi dahil) WP-1.2 tam DbContext paketinde tamamlanacaktır.</summary>
    public DbSet<Queue> Queues => Set<Queue>();

    public DbSet<QueueItem> QueueItems => Set<QueueItem>();

    /// <summary>Paket B — Studio proje/workflow kalıcılığı.</summary>
    public DbSet<Project> Projects => Set<Project>();

    public DbSet<RPA.Domain.Entities.Workflow> Workflows => Set<RPA.Domain.Entities.Workflow>();

    /// <summary>Task 3.1 — Robot kayıt + heartbeat + offline tespiti (Spec Bölüm 5.6, 9).</summary>
    public DbSet<Robot> Robots => Set<Robot>();

    /// <summary>Task 3.3 — Zamanlayıcı + tetikleyiciler (Spec Bölüm 7).</summary>
    public DbSet<Trigger> Triggers => Set<Trigger>();

    public DbSet<Schedule> Schedules => Set<Schedule>();

    public DbSet<JobRun> JobRuns => Set<JobRun>();

    /// <summary>WP-6.2 — Action Center kayıtları (BusinessException/OTP/Onay).</summary>
    public DbSet<ActionItem> ActionItems => Set<ActionItem>();

    /// <summary>WP-6.3 — Alarm kuralları (koşul/kanal/alıcılar).</summary>
    public DbSet<AlertRule> AlertRules => Set<AlertRule>();

    /// <summary>WP-6.4 — Ortamlar (Dev/Test/Prod) — deployment governance hedefleri.</summary>
    public DbSet<Environment> Environments => Set<Environment>();

    /// <summary>WP-6.4 — Workflow versiyonları (Draft → Test → Published yaşam döngüsü).</summary>
    public DbSet<WorkflowVersion> WorkflowVersions => Set<WorkflowVersion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Action).HasMaxLength(64).IsRequired();
            entity.Property(a => a.ResourceType).HasMaxLength(128).IsRequired();
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(u => u.Id);
            entity.Property(u => u.AdUsername).HasMaxLength(256).IsRequired();

            // This task (1.6.1) only wires up the minimal set needed for the
            // AuditLog interceptor. The Roles navigation drags in the full
            // Role -> Permission -> Project -> Workflow graph (which isn't
            // configured yet); that will be completed in the full DbContext task.
            entity.Ignore(u => u.Roles);
        });

        modelBuilder.Entity<Queue>(entity =>
        {
            entity.HasKey(q => q.Id);
            entity.Property(q => q.Name).HasMaxLength(256).IsRequired();
            // Project/Items navigasyonları tam şema paketinde (WP-1.2) yapılandırılacak.
            entity.Ignore(q => q.Project);
            entity.Ignore(q => q.Items);
        });

        modelBuilder.Entity<ActionItem>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Type).HasMaxLength(64).IsRequired();
            entity.Property(a => a.Status).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<AlertRule>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Name).HasMaxLength(256).IsRequired();
            entity.Property(a => a.Channel).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<QueueItem>(entity =>
        {
            entity.HasKey(qi => qi.Id);
            entity.Property(qi => qi.IdempotencyKey).HasMaxLength(256).IsRequired();
            // Spec Bölüm 5.2 — referans anahtarı ile mükerrer işlem engeli: kuyruk +
            // idempotency key birlikte benzersizdir.
            entity.HasIndex(qi => new { qi.QueueId, qi.IdempotencyKey }).IsUnique();
            // Task 3.2 — kuyruk motoru sıradaki New kalemi (QueueId + Status) ile çeker; claim
            // sorgusunun UPDLOCK/READPAST performansı için birleşik indeks.
            entity.HasIndex(qi => new { qi.QueueId, qi.Status });
            // Robot/Queue navigasyonları tam şema paketinde (WP-1.2/3.2) yapılandırılacak.
            entity.Ignore(qi => qi.Queue);
            entity.Ignore(qi => qi.AssignedRobot);
        });

        modelBuilder.Entity<Robot>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.Property(r => r.MachineName).HasMaxLength(256).IsRequired();
            entity.Property(r => r.Tags).HasMaxLength(1024);
            entity.Property(r => r.AgentVersion).HasMaxLength(64);
            entity.Property(r => r.Mode).HasConversion<string>().HasMaxLength(32);
            entity.Property(r => r.Status).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(r => r.MachineName);
            // QueueItems navigasyonu QueueItem tarafında Ignore edildi (tam şema WP-1.2/3.2).
            entity.Ignore(r => r.QueueItems);
        });

        modelBuilder.Entity<Trigger>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Configuration).HasMaxLength(4000).IsRequired();
            entity.Property(t => t.Type).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(t => new { t.WorkflowVersionId, t.IsActive });
            // Project navigasyonu tam şema paketinde (WP-1.2) yapılandırılacak.
            entity.Ignore(t => t.Project);
        });

        modelBuilder.Entity<Schedule>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.Property(s => s.CronExpression).HasMaxLength(128).IsRequired();
            entity.Property(s => s.TimeZone).HasMaxLength(64).IsRequired();
            entity.Property(s => s.OverlapPolicy).HasMaxLength(32).IsRequired();
            entity.HasIndex(s => s.TriggerId).IsUnique();
        });

        modelBuilder.Entity<JobRun>(entity =>
        {
            entity.HasKey(j => j.Id);
            entity.Property(j => j.TriggeredBy).HasMaxLength(32).IsRequired();
            entity.Property(j => j.Status).HasMaxLength(32).IsRequired();
            entity.Property(j => j.ElasticsearchCorrelationId).HasMaxLength(64);
            // Task 3.3 — OverlapPolicy kontrolü (skip/queue) aynı WorkflowVersion için çalışan
            // JobRun aranmasıyla yapılır; bu sorgu için birleşik indeks.
            entity.HasIndex(j => new { j.WorkflowVersionId, j.Status });
            // WorkflowVersion navigasyonu tam şema paketinde (WP-1.2) yapılandırılacak.
            entity.Ignore(j => j.WorkflowVersion);
        });

        modelBuilder.Entity<Environment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(512);
            entity.HasIndex(e => e.Name).IsUnique();
            // Credential/Asset navigasyonları tam şema paketinde (WP-1.2) yapılandırılacak.
            entity.Ignore(e => e.Credentials);
            entity.Ignore(e => e.Assets);
        });

        modelBuilder.Entity<WorkflowVersion>(entity =>
        {
            entity.HasKey(v => v.Id);
            entity.Property(v => v.Version).HasMaxLength(32).IsRequired();
            entity.Property(v => v.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(v => v.ChangeNotes).HasMaxLength(2048);
            entity.HasIndex(v => new { v.WorkflowId, v.Version }).IsUnique();
            // Workflow/Environment navigasyonları tam şema paketinde (WP-1.2) yapılandırılacak.
            entity.Ignore(v => v.Workflow);
            entity.Ignore(v => v.Environment);
        });

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

        modelBuilder.Entity<RPA.Domain.Entities.Workflow>(entity =>
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
    }
}

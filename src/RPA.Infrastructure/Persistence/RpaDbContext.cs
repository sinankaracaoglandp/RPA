using Microsoft.EntityFrameworkCore;
using RPA.Domain.Entities;

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

    /// <summary>Task 3.1 — Robot kayıt + heartbeat + offline tespiti (Spec Bölüm 5.6, 9).</summary>
    public DbSet<Robot> Robots => Set<Robot>();

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

        modelBuilder.Entity<QueueItem>(entity =>
        {
            entity.HasKey(qi => qi.Id);
            entity.Property(qi => qi.IdempotencyKey).HasMaxLength(256).IsRequired();
            // Spec Bölüm 5.2 — referans anahtarı ile mükerrer işlem engeli: kuyruk +
            // idempotency key birlikte benzersizdir.
            entity.HasIndex(qi => new { qi.QueueId, qi.IdempotencyKey }).IsUnique();
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
    }
}

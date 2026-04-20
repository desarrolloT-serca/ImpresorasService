using System.Security.Cryptography;
using ImpresorasService.Domain;
using ImpresorasService.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ImpresorasService.Infrastructure.Persistence;

public class ImpresorasDbContext : DbContext
{
    public ImpresorasDbContext(DbContextOptions<ImpresorasDbContext> options) : base(options)
    {
    }

    public override int SaveChanges()
    {
        BumpPrintJobRowVersionsForConcurrency();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        BumpPrintJobRowVersionsForConcurrency();
        return base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Asigna un nuevo token de concurrencia en cada alta o modificación de PrintJob para que el UPDATE
    /// incluya WHERE RowVersion = @anterior (optimistic locking real frente a múltiples workers).
    /// </summary>
    private void BumpPrintJobRowVersionsForConcurrency()
    {
        foreach (var entry in ChangeTracker.Entries<PrintJob>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.RowVersion is null || entry.Entity.RowVersion.Length == 0)
                    entry.Entity.RowVersion = CreateRowVersionBytes();
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.RowVersion = CreateRowVersionBytes();
            }
        }
    }

    private static byte[] CreateRowVersionBytes()
    {
        var bytes = new byte[8];
        RandomNumberGenerator.Fill(bytes);
        return bytes;
    }

    public DbSet<PrintJob> PrintJobs => Set<PrintJob>();
    public DbSet<PrintJobEvent> PrintJobEvents => Set<PrintJobEvent>();
    public DbSet<SourcePrintJobRecord> SourcePrintJobs => Set<SourcePrintJobRecord>();
    public DbSet<Printer> Printers => Set<Printer>();
    public DbSet<RoutingRule> RoutingRules => Set<RoutingRule>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<DashboardThreshold> DashboardThresholds => Set<DashboardThreshold>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PrintJob>(entity =>
        {
            entity.ToTable("PrintJobs");
            entity.HasKey(x => x.JobId);
            entity.Property(x => x.SourceSystem).HasMaxLength(50).IsRequired();
            entity.Property(x => x.ExternalJobId).HasMaxLength(120).IsRequired();
            entity.Property(x => x.DocumentType).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Channel).HasMaxLength(40).HasDefaultValue("DEFAULT");
            entity.Property(x => x.PdfSha256).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
            entity.Property(x => x.PrinterId);
            entity.Property(x => x.LastErrorCode).HasMaxLength(60);
            entity.Property(x => x.LastErrorMessage).HasMaxLength(1000);
            entity.Property(x => x.RowVersion).IsConcurrencyToken();

            entity.HasIndex(x => new { x.SourceSystem, x.ExternalJobId }).IsUnique();
            entity.HasIndex(x => new { x.Status, x.NextRetryAtUtc });
        });

        modelBuilder.Entity<PrintJobEvent>(entity =>
        {
            entity.ToTable("PrintJobEvents");
            entity.HasKey(x => x.EventId);
            entity.Property(x => x.EventType).HasMaxLength(60).IsRequired();
            entity.Property(x => x.OldStatus).HasConversion<string>().HasMaxLength(40);
            entity.Property(x => x.NewStatus).HasConversion<string>().HasMaxLength(40);
            entity.Property(x => x.ErrorCode).HasMaxLength(60);
            entity.Property(x => x.Message).HasMaxLength(1000);
            entity.Property(x => x.ActorType).HasMaxLength(30).IsRequired();
            entity.Property(x => x.ActorId).HasMaxLength(120);

            entity.HasIndex(x => new { x.JobId, x.OccurredAtUtc });
        });

        modelBuilder.Entity<SourcePrintJobRecord>(entity =>
        {
            entity.ToTable("SourcePrintJobs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SourceSystem).HasMaxLength(50).IsRequired();
            entity.Property(x => x.ExternalJobId).HasMaxLength(120).IsRequired();
            entity.Property(x => x.DocumentType).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Channel).HasMaxLength(40);
            entity.Property(x => x.PdfBlob).IsRequired();
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.Property(x => x.IsProcessed).HasDefaultValue(false);
            entity.Property(x => x.ClaimedBy).HasMaxLength(200);
            entity.Property(x => x.ClaimToken).HasMaxLength(64);

            entity.HasIndex(x => new { x.IsProcessed, x.CreatedAtUtc });
            entity.HasIndex(x => new { x.IsProcessed, x.ClaimedUntilUtc, x.Id });
        });

        modelBuilder.Entity<Printer>(entity =>
        {
            entity.ToTable("Printers");
            entity.HasKey(x => x.PrinterId);
            entity.Property(x => x.PrinterId).ValueGeneratedOnAdd();
            entity.Property(x => x.PrinterName).HasMaxLength(120).IsRequired();
            entity.Property(x => x.SpoolQueue).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Host).HasMaxLength(255);
            entity.Property(x => x.StoreId).IsRequired();
            entity.Property(x => x.IsActive).HasDefaultValue(true);
            entity.Property(x => x.CapabilitiesJson);
            entity.Property(x => x.ConnectionFailuresStreak).HasDefaultValue(0);
            entity.Property(x => x.LastConnectionOk);
            entity.Property(x => x.LastConnectionCheckAtUtc);
            entity.Property(x => x.LastConnectionTransport).HasMaxLength(40);
            entity.Property(x => x.LastConnectionError).HasMaxLength(400);

            entity.HasIndex(x => new { x.StoreId, x.SpoolQueue }).IsUnique();
        });

        modelBuilder.Entity<RoutingRule>(entity =>
        {
            entity.ToTable("RoutingRules");
            entity.HasKey(x => x.RuleId);
            entity.Property(x => x.RuleId).ValueGeneratedOnAdd();
            entity.Property(x => x.DocumentType).HasMaxLength(80);
            entity.Property(x => x.Channel).HasMaxLength(40);
            entity.Property(x => x.CreatedBy).HasMaxLength(120).IsRequired();
            entity.Property(x => x.IsActive).HasDefaultValue(true);

            entity.HasOne(x => x.Printer)
                .WithMany()
                .HasForeignKey(x => x.PrinterId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(x => new { x.IsActive, x.Priority, x.StoreId, x.DocumentType, x.Channel });
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(x => x.UserId);
            entity.Property(x => x.UserId).ValueGeneratedOnAdd();
            entity.Property(x => x.Login).HasMaxLength(80).IsRequired();
            entity.Property(x => x.PasswordHash).HasMaxLength(256).IsRequired();
            entity.Property(x => x.Role).HasMaxLength(40).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(120);

            entity.HasIndex(x => x.Login).IsUnique();
        });

        modelBuilder.Entity<Store>(entity =>
        {
            entity.ToTable("Stores");
            entity.HasKey(x => x.StoreId);
            entity.Property(x => x.StoreId).ValueGeneratedNever();
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.IsActive).HasDefaultValue(true);
            entity.HasIndex(x => x.Name);
        });

        modelBuilder.Entity<DashboardThreshold>(entity =>
        {
            entity.ToTable("DashboardThresholds");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.WarningQueueMin).HasDefaultValue(10);
            entity.Property(x => x.CriticalQueueMin).HasDefaultValue(30);
            entity.Property(x => x.QueueWarningSeverity).HasMaxLength(20).HasDefaultValue("warning").IsRequired();
            entity.Property(x => x.QueueCriticalSeverity).HasMaxLength(20).HasDefaultValue("critical").IsRequired();
            entity.Property(x => x.WarningFailedWithoutRetryMin).HasDefaultValue(1);
            entity.Property(x => x.CriticalFailedWithoutRetryMin).HasDefaultValue(5);
            entity.Property(x => x.FailedWarningSeverity).HasMaxLength(20).HasDefaultValue("warning").IsRequired();
            entity.Property(x => x.FailedCriticalSeverity).HasMaxLength(20).HasDefaultValue("critical").IsRequired();
            entity.Property(x => x.MissingHostMin).HasDefaultValue(1);
            entity.Property(x => x.MissingHostSeverity).HasMaxLength(20).HasDefaultValue("warning").IsRequired();
            entity.Property(x => x.ConnWarningFailuresMin).HasDefaultValue(2);
            entity.Property(x => x.ConnCriticalFailuresMin).HasDefaultValue(3);
            entity.Property(x => x.ConnWarningSeverity).HasMaxLength(20).HasDefaultValue("warning").IsRequired();
            entity.Property(x => x.ConnCriticalSeverity).HasMaxLength(20).HasDefaultValue("critical").IsRequired();
            entity.Property(x => x.UpdatedAtUtc).IsRequired();
        });
    }
}

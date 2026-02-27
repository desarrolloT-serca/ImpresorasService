using ImpresorasService.Domain;
using ImpresorasService.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ImpresorasService.Infrastructure.Persistence;

public class ImpresorasDbContext : DbContext
{
    public ImpresorasDbContext(DbContextOptions<ImpresorasDbContext> options) : base(options)
    {
    }

    public DbSet<PrintJob> PrintJobs => Set<PrintJob>();
    public DbSet<PrintJobEvent> PrintJobEvents => Set<PrintJobEvent>();
    public DbSet<SourcePrintJobRecord> SourcePrintJobs => Set<SourcePrintJobRecord>();
    public DbSet<Printer> Printers => Set<Printer>();
    public DbSet<RoutingRule> RoutingRules => Set<RoutingRule>();
    public DbSet<User> Users => Set<User>();

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

            entity.HasIndex(x => new { x.IsProcessed, x.CreatedAtUtc });
        });

        modelBuilder.Entity<Printer>(entity =>
        {
            entity.ToTable("Printers");
            entity.HasKey(x => x.PrinterId);
            entity.Property(x => x.PrinterId).ValueGeneratedOnAdd();
            entity.Property(x => x.PrinterName).HasMaxLength(120).IsRequired();
            entity.Property(x => x.SpoolQueue).HasMaxLength(200).IsRequired();
            entity.Property(x => x.StoreId).IsRequired();
            entity.Property(x => x.IsActive).HasDefaultValue(true);
            entity.Property(x => x.CapabilitiesJson);

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
    }
}

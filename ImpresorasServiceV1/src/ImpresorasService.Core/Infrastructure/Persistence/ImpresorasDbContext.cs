using System.Security.Cryptography;
using System.Globalization;
using ImpresorasService.Domain;
using ImpresorasService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ImpresorasService.Infrastructure.Persistence;

public class ImpresorasDbContext : DbContext
{
    private static readonly string[] SupportedDateFormats =
    {
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm:ss.fff",
        "yyyy-MM-dd HH:mm:ss.fffffff",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-ddTHH:mm:ss.fff",
        "yyyy-MM-ddTHH:mm:ss.fffffff",
        "dd/MM/yyyy HH:mm:ss",
        "dd/MM/yyyy HH:mm:ss.fff",
        "dd/MM/yyyy HH:mm:ss.fffffff",
        "O"
    };

    private static readonly ValueConverter<DateTimeOffset, string> DateTimeOffsetToStringConverter =
        new(
            toDb => toDb.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            fromDb => ParseDateTimeOffset(fromDb));

    private static readonly ValueConverter<DateTimeOffset?, string?> NullableDateTimeOffsetToStringConverter =
        new(
            toDb => toDb.HasValue
                ? toDb.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                : null,
            fromDb => string.IsNullOrWhiteSpace(fromDb) ? null : ParseDateTimeOffset(fromDb));

    private static readonly ValueConverter<Guid, byte[]> GuidToBytesConverter =
        new(
            toDb => toDb.ToByteArray(),
            fromDb => new Guid(fromDb));

    private static readonly ValueConverter<Guid?, byte[]?> NullableGuidToBytesConverter =
        new(
            toDb => toDb.HasValue ? toDb.Value.ToByteArray() : null,
            fromDb => fromDb == null ? (Guid?)null : new Guid(fromDb));

    public ImpresorasDbContext(DbContextOptions<ImpresorasDbContext> options) : base(options)
    {
    }

    private static DateTimeOffset ParseDateTimeOffset(string value)
    {
        var trimmed = value.Trim();
        var styles = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;

        if (DateTimeOffset.TryParseExact(
                trimmed,
                SupportedDateFormats,
                CultureInfo.InvariantCulture,
                styles,
                out var parsedInvariant))
            return parsedInvariant;

        if (DateTimeOffset.TryParseExact(
                trimmed,
                SupportedDateFormats,
                CultureInfo.GetCultureInfo("es-ES"),
                styles,
                out var parsedEs))
            return parsedEs;

        if (DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, styles, out var fallbackInvariant))
            return fallbackInvariant;

        if (DateTimeOffset.TryParse(trimmed, CultureInfo.GetCultureInfo("es-ES"), styles, out var fallbackEs))
            return fallbackEs;

        throw new FormatException($"No se pudo interpretar la fecha '{value}'.");
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
    public DbSet<TelegramConfig> TelegramConfigs => Set<TelegramConfig>();
    public DbSet<TelegramChat> TelegramChats => Set<TelegramChat>();
    public DbSet<StoreAlertState> StoreAlertStates => Set<StoreAlertState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PrintJob>(entity =>
        {
            entity.ToTable("printer_print_job");
            entity.HasKey(x => x.JobId);
            entity.Property(x => x.JobId).HasColumnName("job_id");
            entity.Property(x => x.JobId).HasConversion(GuidToBytesConverter);
            entity.Property(x => x.SourceSystem).HasColumnName("source_system");
            entity.Property(x => x.ExternalJobId).HasColumnName("external_job_id");
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.DocumentType).HasColumnName("document_type");
            entity.Property(x => x.Channel).HasColumnName("channel");
            entity.Property(x => x.PdfBlob).HasColumnName("pdf_blob");
            entity.Property(x => x.PdfSha256).HasColumnName("pdf_sha256");
            entity.Property(x => x.Status).HasColumnName("status");
            entity.Property(x => x.PrinterId).HasColumnName("printer_id");
            entity.Property(x => x.AttemptCount).HasColumnName("attempt_count");
            entity.Property(x => x.NextRetryAtUtc).HasColumnName("next_retry_at_utc");
            entity.Property(x => x.LastErrorCode).HasColumnName("last_error_code");
            entity.Property(x => x.LastErrorMessage).HasColumnName("last_error_message");
            entity.Property(x => x.CorrelationId).HasColumnName("correlation_id");
            entity.Property(x => x.CorrelationId).HasConversion(GuidToBytesConverter);
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.Property(x => x.RowVersion).HasColumnName("row_version");
            entity.Property(x => x.SourceSystem).HasMaxLength(50).IsRequired();
            entity.Property(x => x.ExternalJobId).HasMaxLength(120).IsRequired();
            entity.Property(x => x.DocumentType).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Channel).HasMaxLength(40).HasDefaultValue("DEFAULT");
            entity.Property(x => x.PdfSha256).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
            entity.Property(x => x.PrinterId);
            entity.Property(x => x.LastErrorCode).HasMaxLength(60);
            entity.Property(x => x.LastErrorMessage).HasMaxLength(1000);
            // En HANA esta columna está en BLOB y comparar en WHERE (optimistic concurrency)
            // puede fallar con "Cannot compare BLocator and BLocator".
            // Mantenemos el valor, pero sin token de concurrencia a nivel EF.

            entity.HasIndex(x => new { x.SourceSystem, x.ExternalJobId }).IsUnique();
            entity.HasIndex(x => new { x.Status, x.NextRetryAtUtc });
        });

        modelBuilder.Entity<PrintJobEvent>(entity =>
        {
            entity.ToTable("printer_print_job_event");
            entity.HasKey(x => x.EventId);
            entity.Property(x => x.EventId).HasColumnName("event_id");
            entity.Property(x => x.JobId).HasColumnName("job_id");
            entity.Property(x => x.JobId).HasConversion(GuidToBytesConverter);
            entity.Property(x => x.EventType).HasColumnName("event_type");
            entity.Property(x => x.OldStatus).HasColumnName("old_status");
            entity.Property(x => x.NewStatus).HasColumnName("new_status");
            entity.Property(x => x.ErrorCode).HasColumnName("error_code");
            entity.Property(x => x.Message).HasColumnName("message");
            entity.Property(x => x.ActorType).HasColumnName("actor_type");
            entity.Property(x => x.ActorId).HasColumnName("actor_id");
            entity.Property(x => x.MetadataJson).HasColumnName("metadata_json");
            entity.Property(x => x.OccurredAtUtc).HasColumnName("occurred_at_utc");
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
            entity.ToTable("printer_source_print_job");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.SourceSystem).HasColumnName("source_system");
            entity.Property(x => x.ExternalJobId).HasColumnName("external_job_id");
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.DocumentType).HasColumnName("document_type");
            entity.Property(x => x.Channel).HasColumnName("channel");
            entity.Property(x => x.PdfBlob).HasColumnName("pdf_blob");
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(x => x.IsProcessed).HasColumnName("is_processed");
            entity.Property(x => x.ClaimedBy).HasColumnName("claimed_by");
            entity.Property(x => x.ClaimedUntilUtc).HasColumnName("claimed_until_utc");
            entity.Property(x => x.ClaimToken).HasColumnName("claim_token");
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
            entity.ToTable("printer_printer");
            entity.HasKey(x => x.PrinterId);
            entity.Property(x => x.PrinterId).HasColumnName("printer_id");
            entity.Property(x => x.PrinterName).HasColumnName("printer_name");
            entity.Property(x => x.SpoolQueue).HasColumnName("spool_queue");
            entity.Property(x => x.Host).HasColumnName("host");
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.IsActive).HasColumnName("is_active");
            entity.Property(x => x.CapabilitiesJson).HasColumnName("capabilities_json");
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.Property(x => x.ConnectionFailuresStreak).HasColumnName("connection_failures_streak");
            entity.Property(x => x.LastConnectionOk).HasColumnName("last_connection_ok");
            entity.Property(x => x.LastConnectionCheckAtUtc).HasColumnName("last_connection_check_at_utc");
            entity.Property(x => x.LastConnectionTransport).HasColumnName("last_connection_transport");
            entity.Property(x => x.LastConnectionError).HasColumnName("last_connection_error");
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
            entity.Property(x => x.IppSupported).HasColumnName("ipp_supported");

            entity.HasIndex(x => new { x.StoreId, x.SpoolQueue }).IsUnique();
        });

        modelBuilder.Entity<RoutingRule>(entity =>
        {
            entity.ToTable("printer_routing_rule");
            entity.HasKey(x => x.RuleId);
            entity.Property(x => x.RuleId).HasColumnName("rule_id");
            entity.Property(x => x.Priority).HasColumnName("priority");
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.DocumentType).HasColumnName("document_type");
            entity.Property(x => x.Channel).HasColumnName("channel");
            entity.Property(x => x.PrinterId).HasColumnName("printer_id");
            entity.Property(x => x.IsActive).HasColumnName("is_active");
            entity.Property(x => x.ValidFromUtc).HasColumnName("valid_from_utc");
            entity.Property(x => x.ValidToUtc).HasColumnName("valid_to_utc");
            entity.Property(x => x.CreatedBy).HasColumnName("created_by");
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
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
            entity.ToTable("printer_user");
            entity.HasKey(x => x.UserId);
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.Login).HasColumnName("login");
            entity.Property(x => x.PasswordHash).HasColumnName("password_hash");
            entity.Property(x => x.Role).HasColumnName("role");
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.DisplayName).HasColumnName("display_name");
            entity.Property(x => x.UserId).ValueGeneratedOnAdd();
            entity.Property(x => x.Login).HasMaxLength(80).IsRequired();
            entity.Property(x => x.PasswordHash).HasMaxLength(256).IsRequired();
            entity.Property(x => x.Role).HasMaxLength(40).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(120);

            entity.HasIndex(x => x.Login).IsUnique();
        });

        modelBuilder.Entity<Store>(entity =>
        {
            entity.ToTable("printer_store");
            entity.HasKey(x => x.StoreId);
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.Name).HasColumnName("name");
            entity.Property(x => x.IsActive).HasColumnName("is_active");
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.Property(x => x.StoreId).ValueGeneratedNever();
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.IsActive).HasDefaultValue(true);
            entity.HasIndex(x => x.Name);
        });

        modelBuilder.Entity<DashboardThreshold>(entity =>
        {
            entity.ToTable("printer_dashboard_threshold");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.WarningQueueMin).HasColumnName("warning_queue_min");
            entity.Property(x => x.CriticalQueueMin).HasColumnName("critical_queue_min");
            entity.Property(x => x.QueueWarningSeverity).HasColumnName("queue_warning_severity");
            entity.Property(x => x.QueueCriticalSeverity).HasColumnName("queue_critical_severity");
            entity.Property(x => x.WarningFailedWithoutRetryMin).HasColumnName("warning_failed_without_retry_min");
            entity.Property(x => x.CriticalFailedWithoutRetryMin).HasColumnName("critical_failed_without_retry_min");
            entity.Property(x => x.FailedWarningSeverity).HasColumnName("failed_warning_severity");
            entity.Property(x => x.FailedCriticalSeverity).HasColumnName("failed_critical_severity");
            entity.Property(x => x.MissingHostMin).HasColumnName("missing_host_min");
            entity.Property(x => x.MissingHostSeverity).HasColumnName("missing_host_severity");
            entity.Property(x => x.ConnWarningFailuresMin).HasColumnName("conn_warning_failures_min");
            entity.Property(x => x.ConnCriticalFailuresMin).HasColumnName("conn_critical_failures_min");
            entity.Property(x => x.ConnWarningSeverity).HasColumnName("conn_warning_severity");
            entity.Property(x => x.ConnCriticalSeverity).HasColumnName("conn_critical_severity");
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
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

        modelBuilder.Entity<TelegramConfig>(entity =>
        {
            entity.ToTable("printer_telegram_config");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(x => x.MinSeverity).HasColumnName("min_severity").HasMaxLength(20).HasDefaultValue("critical").IsRequired();
            entity.Property(x => x.NotifyOnRecovery).HasColumnName("notify_on_recovery").HasDefaultValue(true).IsRequired();
            entity.Property(x => x.CheckIntervalMinutes).HasColumnName("check_interval_minutes").HasDefaultValue(5).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
        });

        modelBuilder.Entity<TelegramChat>(entity =>
        {
            entity.ToTable("printer_telegram_chat");
            entity.HasKey(x => x.ChatId);
            entity.Property(x => x.ChatId).HasColumnName("chat_id").ValueGeneratedNever();
            entity.Property(x => x.Description).HasColumnName("description").HasMaxLength(120);
            entity.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        });

        modelBuilder.Entity<StoreAlertState>(entity =>
        {
            entity.ToTable("printer_alert_state");
            entity.HasKey(x => x.StoreId);
            entity.Property(x => x.StoreId).HasColumnName("store_id").ValueGeneratedNever();
            entity.Property(x => x.LastHealth).HasColumnName("last_health").HasMaxLength(20).HasDefaultValue("healthy").IsRequired();
            entity.Property(x => x.NotifiedHealth).HasColumnName("notified_health").HasMaxLength(20);
            entity.Property(x => x.NotifiedAtUtc).HasColumnName("notified_at_utc");
            entity.Property(x => x.CheckedAtUtc).HasColumnName("checked_at_utc").IsRequired();
        });

        // HANA está persistiendo fechas históricas en formato string no homogéneo (legacy).
        // Forzamos conversión robusta para evitar fallos de materialización en listados y CRUD.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset))
                    property.SetValueConverter(DateTimeOffsetToStringConverter);
                else if (property.ClrType == typeof(DateTimeOffset?))
                    property.SetValueConverter(NullableDateTimeOffsetToStringConverter);
                else if (property.ClrType == typeof(Guid))
                    property.SetValueConverter(GuidToBytesConverter);
                else if (property.ClrType == typeof(Guid?))
                    property.SetValueConverter(NullableGuidToBytesConverter);
            }
        }
    }
}

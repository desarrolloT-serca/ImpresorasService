using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ImpresorasService.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BaselineOrmHana : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DashboardThresholds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    WarningQueueMin = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 10),
                    CriticalQueueMin = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 30),
                    QueueWarningSeverity = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, defaultValue: "warning"),
                    QueueCriticalSeverity = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, defaultValue: "critical"),
                    WarningFailedWithoutRetryMin = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1),
                    CriticalFailedWithoutRetryMin = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 5),
                    FailedWarningSeverity = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, defaultValue: "warning"),
                    FailedCriticalSeverity = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, defaultValue: "critical"),
                    MissingHostMin = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1),
                    MissingHostSeverity = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, defaultValue: "warning"),
                    ConnWarningFailuresMin = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 2),
                    ConnCriticalFailuresMin = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 3),
                    ConnWarningSeverity = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, defaultValue: "warning"),
                    ConnCriticalSeverity = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, defaultValue: "critical"),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DashboardThresholds", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Printers",
                columns: table => new
                {
                    PrinterId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PrinterName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    SpoolQueue = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Host = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    StoreId = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    CapabilitiesJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ConnectionFailuresStreak = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    LastConnectionOk = table.Column<bool>(type: "INTEGER", nullable: true),
                    LastConnectionCheckAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastConnectionTransport = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    LastConnectionError = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Printers", x => x.PrinterId);
                });

            migrationBuilder.CreateTable(
                name: "PrintJobEvents",
                columns: table => new
                {
                    EventId = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    OldStatus = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    NewStatus = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    Message = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ActorType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ActorId = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrintJobEvents", x => x.EventId);
                });

            migrationBuilder.CreateTable(
                name: "PrintJobs",
                columns: table => new
                {
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceSystem = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ExternalJobId = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    StoreId = table.Column<int>(type: "INTEGER", nullable: false),
                    DocumentType = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false, defaultValue: "DEFAULT"),
                    PdfBlob = table.Column<byte[]>(type: "BLOB", nullable: false),
                    PdfSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    PrinterId = table.Column<int>(type: "INTEGER", nullable: true),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    NextRetryAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastErrorCode = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    LastErrorMessage = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CorrelationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrintJobs", x => x.JobId);
                });

            migrationBuilder.CreateTable(
                name: "SourcePrintJobs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceSystem = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ExternalJobId = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    StoreId = table.Column<int>(type: "INTEGER", nullable: false),
                    DocumentType = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    PdfBlob = table.Column<byte[]>(type: "BLOB", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    IsProcessed = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    ClaimedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ClaimedUntilUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ClaimToken = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourcePrintJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Stores",
                columns: table => new
                {
                    StoreId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stores", x => x.StoreId);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Login = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    StoreId = table.Column<int>(type: "INTEGER", nullable: true),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "RoutingRules",
                columns: table => new
                {
                    RuleId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    StoreId = table.Column<int>(type: "INTEGER", nullable: true),
                    DocumentType = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    PrinterId = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    ValidFromUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ValidToUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoutingRules", x => x.RuleId);
                    table.ForeignKey(
                        name: "FK_RoutingRules_Printers_PrinterId",
                        column: x => x.PrinterId,
                        principalTable: "Printers",
                        principalColumn: "PrinterId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Printers_StoreId_SpoolQueue",
                table: "Printers",
                columns: new[] { "StoreId", "SpoolQueue" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrintJobEvents_JobId_OccurredAtUtc",
                table: "PrintJobEvents",
                columns: new[] { "JobId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PrintJobs_SourceSystem_ExternalJobId",
                table: "PrintJobs",
                columns: new[] { "SourceSystem", "ExternalJobId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrintJobs_Status_NextRetryAtUtc",
                table: "PrintJobs",
                columns: new[] { "Status", "NextRetryAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RoutingRules_IsActive_Priority_StoreId_DocumentType_Channel",
                table: "RoutingRules",
                columns: new[] { "IsActive", "Priority", "StoreId", "DocumentType", "Channel" });

            migrationBuilder.CreateIndex(
                name: "IX_RoutingRules_PrinterId",
                table: "RoutingRules",
                column: "PrinterId");

            migrationBuilder.CreateIndex(
                name: "IX_SourcePrintJobs_IsProcessed_ClaimedUntilUtc_Id",
                table: "SourcePrintJobs",
                columns: new[] { "IsProcessed", "ClaimedUntilUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_SourcePrintJobs_IsProcessed_CreatedAtUtc",
                table: "SourcePrintJobs",
                columns: new[] { "IsProcessed", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Stores_Name",
                table: "Stores",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Login",
                table: "Users",
                column: "Login",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DashboardThresholds");

            migrationBuilder.DropTable(
                name: "PrintJobEvents");

            migrationBuilder.DropTable(
                name: "PrintJobs");

            migrationBuilder.DropTable(
                name: "RoutingRules");

            migrationBuilder.DropTable(
                name: "SourcePrintJobs");

            migrationBuilder.DropTable(
                name: "Stores");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Printers");
        }
    }
}

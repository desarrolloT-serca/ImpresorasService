using Microsoft.EntityFrameworkCore;

namespace ImpresorasService.Infrastructure.Persistence;

/// <summary>
/// Parches de esquema SQLite para BDs creadas antes de nuevas columnas (EnsureCreated no altera tablas existentes).
/// </summary>
public static class SqliteSchemaPatches
{
    public static async Task EnsurePrintJobsRowVersionColumnAsync(
        ImpresorasDbContext db,
        CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsSqlite())
            return;

        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('PrintJobs') WHERE name='RowVersion'";
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
        if (count == 0)
        {
            cmd.CommandText = "ALTER TABLE PrintJobs ADD COLUMN RowVersion BLOB NULL";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public static async Task EnsureSourcePrintJobsClaimColumnsAsync(
        ImpresorasDbContext db,
        CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsSqlite())
            return;

        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();

        async Task EnsureColumnAsync(string columnName, string ddl)
        {
            cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('SourcePrintJobs') WHERE name='{columnName}'";
            var c = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
            if (c == 0)
            {
                cmd.CommandText = ddl;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await EnsureColumnAsync("ClaimedBy", "ALTER TABLE SourcePrintJobs ADD COLUMN ClaimedBy TEXT NULL");
        await EnsureColumnAsync("ClaimedUntilUtc", "ALTER TABLE SourcePrintJobs ADD COLUMN ClaimedUntilUtc TEXT NULL");
        await EnsureColumnAsync("ClaimToken", "ALTER TABLE SourcePrintJobs ADD COLUMN ClaimToken TEXT NULL");
    }
}

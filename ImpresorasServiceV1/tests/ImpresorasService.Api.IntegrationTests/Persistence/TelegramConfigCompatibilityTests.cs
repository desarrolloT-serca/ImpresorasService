using ImpresorasService.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests.Persistence;

public sealed class TelegramConfigCompatibilityTests
{
    [Fact]
    public async Task TelegramConfig_CanReadLegacySchemaWithoutEnabledColumn()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE printer_telegram_config (
                    id INTEGER NOT NULL PRIMARY KEY,
                    min_severity TEXT NOT NULL,
                    notify_on_recovery INTEGER NOT NULL,
                    check_interval_minutes INTEGER NOT NULL,
                    updated_at_utc TEXT NOT NULL
                );

                INSERT INTO printer_telegram_config
                    (id, min_severity, notify_on_recovery, check_interval_minutes, updated_at_utc)
                VALUES
                    (1, 'critical', 1, 5, '2026-06-30 00:00:00');
                """;
            await command.ExecuteNonQueryAsync();
        }

        var options = new DbContextOptionsBuilder<ImpresorasDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new ImpresorasDbContext(options);

        var config = await db.TelegramConfigs.AsNoTracking().SingleAsync(x => x.Id == 1);

        Assert.Equal("critical", config.MinSeverity);
        Assert.True(config.NotifyOnRecovery);
        Assert.Equal(5, config.CheckIntervalMinutes);
    }
}

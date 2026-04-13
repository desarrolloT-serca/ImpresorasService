using System;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ImpresorasService.Api.IntegrationTests;

internal static class SqliteTestDbHelper
{
    public sealed class SqliteTestDbSetup : IDisposable
    {
        public ImpresorasDbContext Db { get; }
        private readonly SqliteConnection _connection;

        public SqliteTestDbSetup(ImpresorasDbContext db, SqliteConnection connection)
        {
            Db = db;
            _connection = connection;
        }

        public void Dispose()
        {
            Db.Dispose();
            _connection.Dispose();
        }
    }

    public static SqliteTestDbSetup CreateOpenSqliteInMemory()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<ImpresorasDbContext>()
            .UseSqlite(connection)
            .Options;

        var db = new ImpresorasDbContext(options);
        db.Database.EnsureCreated();

        return new SqliteTestDbSetup(db, connection);
    }
}


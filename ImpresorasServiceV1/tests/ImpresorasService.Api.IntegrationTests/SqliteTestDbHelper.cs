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
        public SqliteConnection Connection => _connection;
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

        var db = NewContext(connection);
        db.Database.EnsureCreated();

        return new SqliteTestDbSetup(db, connection);
    }

    /// <summary>
    /// Otro <see cref="ImpresorasDbContext"/> sobre la MISMA conexión, para simular un segundo
    /// proceso: contextos distintos con su propio seguimiento de entidades, pero una sola base de
    /// datos. Es lo que hace falta para ejercitar la exclusión entre Workers.
    /// </summary>
    public static ImpresorasDbContext NewContext(SqliteConnection connection)
        => new(new DbContextOptionsBuilder<ImpresorasDbContext>().UseSqlite(connection).Options);
}


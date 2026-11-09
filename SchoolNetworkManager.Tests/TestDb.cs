using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Services;

namespace SchoolNetworkManager.Tests;

/// <summary>Βάση SQLite στη μνήμη, δημιουργημένη όπως στο Program.cs (EnsureCreated + DbSchema.Upgrade).</summary>
public static class TestDb
{
    private static readonly object InitLock = new();
    private static bool _protectorReady;

    public static (AppDbContext Db, SqliteConnection Conn) Create()
    {
        lock (InitLock)
        {
            if (!_protectorReady)
            {
                PasswordProtector.Initialize(new EphemeralDataProtectionProvider());
                _protectorReady = true;
            }
        }

        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        DbSchema.Upgrade(db);
        return (db, conn);
    }

    public static bool TableExists(SqliteConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$n";
        cmd.Parameters.AddWithValue("$n", table);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    public static bool ColumnExists(SqliteConnection conn, string table, string column)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name=$c";
        cmd.Parameters.AddWithValue("$c", column);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }
}

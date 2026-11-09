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
        var (db, conn) = CreateWithoutUpgrade();
        DbSchema.Upgrade(db);
        return (db, conn);
    }

    /// <summary>Μόνο EnsureCreated, χωρίς DbSchema.Upgrade.</summary>
    public static (AppDbContext Db, SqliteConnection Conn) CreateWithoutUpgrade()
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
        return (db, conn);
    }

    public static bool IndexExists(SqliteConnection conn, string index)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name=$n";
        cmd.Parameters.AddWithValue("$n", index);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    public static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
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

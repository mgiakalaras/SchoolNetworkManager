using SchoolNetworkManager.Services;
using Xunit;

namespace SchoolNetworkManager.Tests;

public class SchemaTests
{
    [Fact]
    public void Upgrade_CreatesNewTablesAndColumns_AndIsRepeatable()
    {
        var (db, conn) = TestDb.Create();
        using (db) using (conn)
        {
            DbSchema.Upgrade(db);                       // second run must not throw
            Assert.True(TestDb.TableExists(conn, "Buildings"));
            Assert.True(TestDb.TableExists(conn, "Floors"));
            Assert.True(TestDb.TableExists(conn, "RoomTypes"));
            Assert.True(TestDb.TableExists(conn, "RoomCapacities"));
            Assert.True(TestDb.ColumnExists(conn, "Rooms", "FloorId"));
            Assert.True(TestDb.ColumnExists(conn, "Computers", "RoomId"));
            Assert.True(TestDb.ColumnExists(conn, "AppSettings", "RoomsMigratedAt"));
        }
    }
}

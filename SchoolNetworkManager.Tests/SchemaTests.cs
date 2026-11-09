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

    private static readonly (string Table, string Column)[] NewColumns =
    {
        ("Rooms", "FloorId"), ("Rooms", "RoomTypeId"), ("Rooms", "SortOrder"),
        ("Rooms", "PlanX"), ("Rooms", "PlanY"), ("Rooms", "PlanW"), ("Rooms", "PlanH"),
        ("Computers", "RoomId"), ("AppSettings", "RoomsMigratedAt"),
    };
    private static readonly string[] NewTables = { "Buildings", "Floors", "RoomTypes", "RoomCapacities" };
    private static readonly string[] NewIndexes =
    {
        "IX_Floors_BuildingId", "IX_Rooms_FloorId", "IX_Computers_RoomId", "IX_RoomCapacities_RoomId_MachineType",
    };

    [Fact]
    public void Upgrade_AddsSchemaToPreFeatureDatabase()
    {
        var (db, conn) = TestDb.CreateWithoutUpgrade();
        using (db) using (conn)
        {
            // Turn the EF-created DB into the pre-feature shape.
            foreach (var i in NewIndexes) TestDb.Exec(conn, $"DROP INDEX IF EXISTS {i}");
            foreach (var t in NewTables) TestDb.Exec(conn, $"DROP TABLE {t}");
            foreach (var (t, c) in NewColumns) TestDb.Exec(conn, $"ALTER TABLE {t} DROP COLUMN {c}");

            foreach (var t in NewTables) Assert.False(TestDb.TableExists(conn, t));
            foreach (var (t, c) in NewColumns) Assert.False(TestDb.ColumnExists(conn, t, c));

            DbSchema.Upgrade(db);

            foreach (var t in NewTables) Assert.True(TestDb.TableExists(conn, t), t);
            foreach (var (t, c) in NewColumns) Assert.True(TestDb.ColumnExists(conn, t, c), $"{t}.{c}");
            foreach (var i in NewIndexes) Assert.True(TestDb.IndexExists(conn, i), i);

            // Raw rows must be readable through the EF model (names/types match).
            TestDb.Exec(conn, "INSERT INTO Buildings (Name, Description, SortOrder) VALUES ('B1', 'd', 2)");
            TestDb.Exec(conn, "INSERT INTO Floors (BuildingId, Name, Level, SortOrder, PlanImagePath, AspectRatio) VALUES (1, 'F1', -1, 3, 'p.png', 2.5)");
            TestDb.Exec(conn, "INSERT INTO RoomTypes (Name, Icon, Color, SortOrder) VALUES ('T1', 'x', '#fff', 4)");
            TestDb.Exec(conn, "INSERT INTO RoomCapacities (RoomId, MachineType, PlannedCount) VALUES (7, 'PC', 12)");

            var b = db.Buildings.Single();
            Assert.Equal(("B1", "d", 2), (b.Name, b.Description, b.SortOrder));
            var f = db.Floors.Single();
            Assert.Equal((1, "F1", -1, 3, "p.png", 2.5), (f.BuildingId, f.Name, f.Level, f.SortOrder, f.PlanImagePath, f.AspectRatio));
            var rt = db.RoomTypes.Single();
            Assert.Equal(("T1", "x", "#fff", 4), (rt.Name, rt.Icon, rt.Color, rt.SortOrder));
            var rc = db.RoomCapacities.Single();
            Assert.Equal((7, "PC", 12), (rc.RoomId, rc.MachineType, rc.PlannedCount));
        }
    }
}

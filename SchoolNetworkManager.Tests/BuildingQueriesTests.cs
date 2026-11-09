using Xunit;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;
using SchoolNetworkManager.Services;

namespace SchoolNetworkManager.Tests;

public class BuildingQueriesTests
{
    private static int _ip;
    private static Computer Pc(int roomId, bool? online) =>
        new() { Name = "PC", RoomId = roomId, IsOnline = online, MachineType = "Υπολογιστής", IpAddress = "10.1.0." + System.Threading.Interlocked.Increment(ref _ip) };

    [Fact]
    public async Task LoadAsync_TotalsRollUpFloorToBuilding()
    {
        var (db, conn) = TestDb.Create();
        using var _ = conn; using var __ = db;
        var b = new Building { Name = "Κτίριο" };
        db.Buildings.Add(b);
        await db.SaveChangesAsync();
        var f1 = new Floor { BuildingId = b.Id, Name = "Ισόγειο", Level = 0 };
        var f2 = new Floor { BuildingId = b.Id, Name = "1ος", Level = 1 };
        db.Floors.AddRange(f2, f1);
        await db.SaveChangesAsync();

        var svc = new RoomService(db);
        var r1 = await svc.CreateAsync("Α1", f1.Id);
        var r2 = await svc.CreateAsync("Α2", f2.Id);
        await svc.SetCapacitiesAsync(r1.Id, new[] { ("Υπολογιστής", 3) });
        await svc.SetCapacitiesAsync(r2.Id, new[] { ("Υπολογιστής", 2) });
        db.Computers.AddRange(Pc(r1.Id, true), Pc(r1.Id, false), Pc(r2.Id, true));
        await db.SaveChangesAsync();

        var (buildings, unplaced) = await BuildingQueries.LoadAsync(db);

        Assert.Empty(unplaced);
        var bv = Assert.Single(buildings);
        Assert.Equal(new[] { "Ισόγειο", "1ος" }, bv.Floors.Select(f => f.Floor.Name));
        var total = Assert.Single(bv.Totals);
        Assert.Equal(5, total.Planned);
        Assert.Equal(3, total.Actual);
        var card = bv.Floors[0].Rooms.Single();
        Assert.Equal(1, card.Online);
        Assert.Equal(2, card.Devices);
        Assert.Equal(3, Assert.Single(bv.Floors[0].Totals).Planned);
    }

    [Fact]
    public async Task LoadAsync_OnlineAndDevicesIgnoreDynamicAndEmptySlots()
    {
        var (db, conn) = TestDb.Create();
        using var _ = conn; using var __ = db;
        var room = await new RoomService(db).CreateAsync("Α1");
        var dyn = Pc(room.Id, true); dyn.IsDynamic = true;
        var empty = Pc(room.Id, true); empty.Name = "EMPTY";
        db.Computers.AddRange(Pc(room.Id, true), dyn, empty);
        await db.SaveChangesAsync();

        var (_, unplaced) = await BuildingQueries.LoadAsync(db);

        var card = Assert.Single(unplaced);
        Assert.Equal(1, card.Online);
        Assert.Equal(1, card.Devices);
        Assert.Equal(1, Assert.Single(card.Counts).Actual);
    }

    [Fact]
    public async Task LoadAsync_UnplacedRoomsSeparate()
    {
        var (db, conn) = TestDb.Create();
        using var _ = conn; using var __ = db;
        await new RoomService(db).CreateAsync("Χωρίς όροφο");

        var (buildings, unplaced) = await BuildingQueries.LoadAsync(db);

        Assert.Equal("Χωρίς όροφο", Assert.Single(unplaced).Room.Name);
        Assert.All(buildings, b => Assert.All(b.Floors, f => Assert.Empty(f.Rooms)));
    }

    private static async Task<(int BuildingId, int FloorId)> Seed(AppDbContext db, bool withRoom)
    {
        var b = new Building { Name = "Κ" };
        db.Buildings.Add(b);
        await db.SaveChangesAsync();
        var f = new Floor { BuildingId = b.Id, Name = "Ο" };
        db.Floors.Add(f);
        await db.SaveChangesAsync();

        if (withRoom)
        {
            await new RoomService(db).CreateAsync("Χ", f.Id);
        }

        return (b.Id, f.Id);
    }

    [Fact]
    public async Task DeleteFloor_RefusedWhenRoomsPresent()
    {
        var (db, conn) = TestDb.Create();
        using var _ = conn; using var __ = db;
        var (_, floorId) = await Seed(db, true);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => BuildingQueries.DeleteFloorAsync(db, floorId));
        Assert.Contains("1 χώροι", ex.Message);
        Assert.NotNull(await db.Floors.FindAsync(floorId));

        var (_, emptyFloor) = await Seed(db, false);
        await BuildingQueries.DeleteFloorAsync(db, emptyFloor);
        Assert.Null(await db.Floors.FindAsync(emptyFloor));
    }

    [Fact]
    public async Task DeleteBuilding_RefusedWhenRoomsPresent()
    {
        var (db, conn) = TestDb.Create();
        using var _ = conn; using var __ = db;
        var (buildingId, _) = await Seed(db, true);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => BuildingQueries.DeleteBuildingAsync(db, buildingId));
        Assert.Contains("1 χώροι", ex.Message);
        Assert.NotNull(await db.Buildings.FindAsync(buildingId));

        var (emptyB, _) = await Seed(db, false);
        await BuildingQueries.DeleteBuildingAsync(db, emptyB);
        Assert.Null(await db.Buildings.FindAsync(emptyB));
        Assert.Empty(db.Floors.Where(f => f.BuildingId == emptyB));
    }
}

using Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;
using SchoolNetworkManager.Services;

namespace SchoolNetworkManager.Tests;

public class RoomMigrationTests
{
    private static int _ip;

    private static Computer Pc(string name, string? location) =>
        new() { Name = name, Location = location, IpAddress = "10.1.0." + System.Threading.Interlocked.Increment(ref _ip) };

    private static (AppDbContext Db, Microsoft.Data.Sqlite.SqliteConnection Conn) NewDb()
    {
        var (db, conn) = TestDb.Create();
        // Το Program.cs δημιουργεί τη γραμμή ρυθμίσεων πριν από τη μετατροπή
        db.AppSettings.Add(new AppSettings { Id = 1, SchoolTitle = "Test" });
        db.SaveChanges();
        return (db, conn);
    }

    private static Task<bool> Run(AppDbContext db, Func<Task>? backup = null, Func<Task>? afterRooms = null) =>
        RoomMigration.RunAsync(db, backup ?? (() => Task.CompletedTask), NullLogger.Instance, afterRooms);

    [Fact]
    public async Task Run_CreatesDefaultBuildingFloorAndTypes()
    {
        var (db, conn) = NewDb();
        using var _ = conn; using var __ = db;

        Assert.True(await Run(db));

        var building = Assert.Single(await db.Buildings.ToListAsync());
        Assert.Equal("Κτίριο 1", building.Name);
        var floor = Assert.Single(await db.Floors.ToListAsync());
        Assert.Equal("Χωρίς όροφο", floor.Name);
        Assert.Equal(0, floor.Level);
        Assert.Equal(building.Id, floor.BuildingId);
        var types = await db.RoomTypes.OrderBy(t => t.SortOrder).ThenBy(t => t.Id).ToListAsync();
        Assert.Equal(9, types.Count);
        Assert.Equal(RoomTypeDefaults.All.Select(t => t.Name), types.Select(t => t.Name));
        Assert.NotNull((await db.AppSettings.FirstAsync(s => s.Id == 1)).RoomsMigratedAt);
    }

    [Fact]
    public async Task Run_LinksDevicesByNormalisedName()
    {
        var (db, conn) = NewDb();
        using var _ = conn; using var __ = db;
        db.Computers.AddRange(Pc("A", "Lab 1"), Pc("B", " lab 1"), Pc("C", "LAB 1 "));
        await db.SaveChangesAsync();

        await Run(db);

        var room = Assert.Single(await db.Rooms.ToListAsync());
        Assert.Equal("Lab 1", room.Name);
        Assert.Null(room.FloorId);
        var pcs = await db.Computers.ToListAsync();
        Assert.All(pcs, c => Assert.Equal(room.Id, c.RoomId));
        Assert.All(pcs, c => Assert.Equal("Lab 1", c.Location));
    }

    [Fact]
    public async Task Run_KeepsExistingEmptyRoomsUnplaced()
    {
        var (db, conn) = NewDb();
        using var _ = conn; using var __ = db;
        db.Rooms.Add(new Room { Name = "Αποθήκη" });
        await db.SaveChangesAsync();

        await Run(db);

        var room = Assert.Single(await db.Rooms.ToListAsync());
        Assert.Equal("Αποθήκη", room.Name);
        Assert.Null(room.FloorId);
    }

    [Fact]
    public async Task Run_BlankLocationMeansNoRoom()
    {
        var (db, conn) = NewDb();
        using var _ = conn; using var __ = db;
        db.Computers.Add(Pc("A", "   "));
        await db.SaveChangesAsync();

        await Run(db);

        var pc = await db.Computers.SingleAsync();
        Assert.Null(pc.RoomId);
        Assert.Null(pc.Location);
        Assert.Empty(await db.Rooms.ToListAsync());
    }

    [Fact]
    public async Task Run_MergesCollidingRooms()
    {
        var (db, conn) = NewDb();
        using var _ = conn; using var __ = db;
        db.Rooms.AddRange(new Room { Name = "Lab 1" }, new Room { Name = "lab 1 " });
        db.Computers.AddRange(Pc("A", "Lab 1"), Pc("B", "lab 1 "));
        await db.SaveChangesAsync();
        var keepId = (await db.Rooms.OrderBy(r => r.Id).FirstAsync()).Id;

        await Run(db);

        var room = Assert.Single(await db.Rooms.ToListAsync());
        Assert.Equal(keepId, room.Id);
        Assert.Equal("Lab 1", room.Name);
        Assert.All(await db.Computers.ToListAsync(), c => Assert.Equal(keepId, c.RoomId));
    }

    [Fact]
    public async Task Run_ExistingRoomNameWinsOverDeviceVariants()
    {
        var (db, conn) = NewDb();
        using var _ = conn; using var __ = db;
        db.Rooms.Add(new Room { Name = "Εργαστηριο 1" });
        db.Computers.AddRange(Pc("A", "ΕΡΓΑΣΤΗΡΙΟ 1"), Pc("B", "ΕΡΓΑΣΤΗΡΙΟ 1"), Pc("C", "εργαστηριο 1"));
        await db.SaveChangesAsync();

        await Run(db);

        var room = Assert.Single(await db.Rooms.ToListAsync());
        Assert.Equal("Εργαστηριο 1", room.Name);
        Assert.All(await db.Computers.ToListAsync(), c => Assert.Equal("Εργαστηριο 1", c.Location));
    }

    [Fact]
    public async Task Run_MergesAccentVariants()
    {
        var (db, conn) = NewDb();
        using var _ = conn; using var __ = db;
        db.Computers.AddRange(Pc("A", "Εργαστήριο 1"), Pc("B", "ΕΡΓΑΣΤΗΡΙΟ 1"));
        db.PlanItems.Add(new PlanItem { Location = "ΕΡΓΑΣΤΗΡΙΟ 1", Label = "x", Kind = "label" });
        await db.SaveChangesAsync();

        await Run(db);

        var room = Assert.Single(await db.Rooms.ToListAsync());
        Assert.Equal("Εργαστήριο 1", room.Name);
        Assert.All(await db.Computers.ToListAsync(), c => Assert.Equal(room.Id, c.RoomId));
        Assert.Equal("Εργαστήριο 1", (await db.PlanItems.SingleAsync()).Location);
    }

    [Fact]
    public async Task Run_NormalisesPlanAndClassLocations()
    {
        var (db, conn) = NewDb();
        using var _ = conn; using var __ = db;
        db.Rooms.AddRange(new Room { Name = "Lab 1" }, new Room { Name = "LAB 1" });
        db.Computers.Add(Pc("A", "Lab 2"));
        db.PlanItems.AddRange(
            new PlanItem { Location = " lab 1", Label = "x", Kind = "label" },
            new PlanItem { Location = "LAB 1", Label = "y", Kind = "label" },
            new PlanItem { Location = "lab 2 ", Label = "z", Kind = "label" },
            new PlanItem { Location = "Unknown", Label = "w", Kind = "label" });
        db.RoomPlans.Add(new RoomPlan { Location = "lab 1 " });
        db.SchoolClasses.Add(new SchoolClass { Location = "LAB 1", Name = "Β1" });
        await db.SaveChangesAsync();

        await Run(db);

        Assert.Equal(new[] { "Lab 1", "Lab 1", "Lab 2", "Unknown" },
            (await db.PlanItems.OrderBy(p => p.Id).ToListAsync()).Select(p => p.Location));
        Assert.Equal("Lab 1", (await db.RoomPlans.SingleAsync()).Location);
        Assert.Equal("Lab 1", (await db.SchoolClasses.SingleAsync()).Location);
    }

    [Fact]
    public async Task Run_IsIdempotent()
    {
        var (db, conn) = NewDb();
        using var _ = conn; using var __ = db;
        db.Computers.AddRange(Pc("A", "Lab 1"), Pc("B", "Lab 2"));
        await db.SaveChangesAsync();
        var backups = 0;

        Assert.True(await Run(db, () => { backups++; return Task.CompletedTask; }));
        var rooms = await db.Rooms.CountAsync();
        var buildings = await db.Buildings.CountAsync();
        var types = await db.RoomTypes.CountAsync();

        Assert.False(await Run(db, () => { backups++; return Task.CompletedTask; }));

        Assert.Equal(1, backups);
        Assert.Equal(rooms, await db.Rooms.CountAsync());
        Assert.Equal(buildings, await db.Buildings.CountAsync());
        Assert.Equal(types, await db.RoomTypes.CountAsync());
    }

    [Fact]
    public async Task Run_SkipsWhenBackupFails()
    {
        var (db, conn) = NewDb();
        using var _ = conn; using var __ = db;
        db.Computers.Add(Pc("A", "Lab 1"));
        await db.SaveChangesAsync();

        var result = await Run(db, () => throw new IOException("disk full"));

        Assert.False(result);
        Assert.Null((await db.AppSettings.FirstAsync(s => s.Id == 1)).RoomsMigratedAt);
        Assert.Equal(0, await db.Buildings.CountAsync());
        Assert.Equal(0, await db.Rooms.CountAsync());
    }

    [Fact]
    public async Task Run_RollsBackOnFailure()
    {
        var (db, conn) = NewDb();
        using var _ = conn; using var __ = db;
        db.Computers.AddRange(Pc("A", "Lab 1"), Pc("B", "Lab 2"));
        await db.SaveChangesAsync();

        var result = await Run(db, afterRooms: () => throw new InvalidOperationException("boom"));

        Assert.False(result);
        Assert.Equal(0, await db.Buildings.CountAsync());
        Assert.Equal(0, await db.Floors.CountAsync());
        Assert.Equal(0, await db.RoomTypes.CountAsync());
        Assert.Equal(0, await db.Rooms.CountAsync());
        Assert.All(await db.Computers.AsNoTracking().ToListAsync(), c => Assert.Null(c.RoomId));
        Assert.Null((await db.AppSettings.AsNoTracking().FirstAsync(s => s.Id == 1)).RoomsMigratedAt);

        // Μετά την αποτυχία, νέα εκτέλεση πετυχαίνει κανονικά
        Assert.True(await Run(db));
        Assert.Equal(2, await db.Rooms.CountAsync());
    }
}

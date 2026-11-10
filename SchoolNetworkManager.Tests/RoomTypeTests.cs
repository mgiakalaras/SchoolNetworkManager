using Xunit;
using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;
using SchoolNetworkManager.Services;

namespace SchoolNetworkManager.Tests;

public class RoomTypeTests
{
    private static async Task SeedDefaultsAsync(AppDbContext db)
    {
        var order = 0;
        foreach (var (name, icon) in RoomTypeDefaults.All)
        {
            db.RoomTypes.Add(new RoomType { Name = name, Icon = icon, SortOrder = order++ });
        }
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Delete_RefusedWhenInUse()
    {
        var (db, conn) = TestDb.Create();
        using var _ = conn; using var __ = db;
        var type = new RoomType { Name = "Τύπος Δοκιμής", SortOrder = 100 };
        db.RoomTypes.Add(type);
        await db.SaveChangesAsync();
        db.Rooms.Add(new Room { Name = "Αίθουσα 1", RoomTypeId = type.Id });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => RoomTypeOps.DeleteAsync(db, type.Id));
        Assert.True(await db.RoomTypes.AnyAsync(t => t.Id == type.Id));

        // Χωρίς χώρους διαγράφεται κανονικά
        db.Rooms.RemoveRange(db.Rooms.Where(r => r.RoomTypeId == type.Id));
        await db.SaveChangesAsync();
        await RoomTypeOps.DeleteAsync(db, type.Id);
        Assert.False(await db.RoomTypes.AnyAsync(t => t.Id == type.Id));
    }

    [Fact]
    public async Task Save_RejectsDuplicateNameIgnoringCase()
    {
        var (db, conn) = TestDb.Create();
        using var _ = conn; using var __ = db;
        await SeedDefaultsAsync(db);
        var before = await db.RoomTypes.CountAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => RoomTypeOps.SaveAsync(db, null, "  γραφείο ", "x", null));
        Assert.Equal(before, await db.RoomTypes.CountAsync());

        // Η επεξεργασία του ίδιου τύπου με το ίδιο όνομα επιτρέπεται
        var office = await db.RoomTypes.FirstAsync(t => t.Name == "Γραφείο");
        await RoomTypeOps.SaveAsync(db, office.Id, "ΓΡΑΦΕΙΟ", "🗂️", null);
        Assert.Equal("ΓΡΑΦΕΙΟ", (await db.RoomTypes.FindAsync(office.Id))!.Name);
    }

    [Fact]
    public async Task Rename_KeepsRoomsLinked()
    {
        var (db, conn) = TestDb.Create();
        using var _ = conn; using var __ = db;
        await SeedDefaultsAsync(db);
        var type = await db.RoomTypes.FirstAsync(t => t.Name == "Βιβλιοθήκη");
        db.Rooms.Add(new Room { Name = "Αίθουσα 1", RoomTypeId = type.Id });
        await db.SaveChangesAsync();

        await RoomTypeOps.SaveAsync(db, type.Id, "Βιβλιοθήκη & αναγνωστήριο", "📖", "#336699");

        var room = await db.Rooms.SingleAsync();
        Assert.Equal(type.Id, room.RoomTypeId);
        var renamed = await db.RoomTypes.FindAsync(type.Id);
        Assert.Equal("Βιβλιοθήκη & αναγνωστήριο", renamed!.Name);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#12")]
    [InlineData("#12345")]
    [InlineData("#ggg")]
    [InlineData("red; background:url(x)")]
    [InlineData("#fff\" onload=\"x")]
    public async Task Save_RejectsInvalidColor(string color)
    {
        var (db, conn) = TestDb.Create();
        using var _ = conn; using var __ = db;

        await Assert.ThrowsAsync<InvalidOperationException>(() => RoomTypeOps.SaveAsync(db, null, "Νέος τύπος", null, color));
        Assert.False(await db.RoomTypes.AnyAsync(t => t.Name == "Νέος τύπος"));
    }

    [Theory]
    [InlineData("#abc")]
    [InlineData("#A1b2C3")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Save_AcceptsHexColor(string? color)
    {
        var (db, conn) = TestDb.Create();
        using var _ = conn; using var __ = db;

        await RoomTypeOps.SaveAsync(db, null, "Νέος τύπος", " ★ ", color);

        var saved = await db.RoomTypes.SingleAsync(t => t.Name == "Νέος τύπος");
        Assert.Equal(string.IsNullOrEmpty(color) ? null : color, saved.Color);
        Assert.Equal("★", saved.Icon);
    }

    [Fact]
    public async Task Move_SwapsWithNeighbour()
    {
        var (db, conn) = TestDb.Create();
        using var _ = conn; using var __ = db;
        await SeedDefaultsAsync(db);
        var ordered = await db.RoomTypes.OrderBy(t => t.SortOrder).ThenBy(t => t.Id).Select(t => t.Id).ToListAsync();

        await RoomTypeOps.MoveAsync(db, ordered[1], -1);

        var after = await db.RoomTypes.OrderBy(t => t.SortOrder).ThenBy(t => t.Id).Select(t => t.Id).ToListAsync();
        Assert.Equal(ordered[1], after[0]);
        Assert.Equal(ordered[0], after[1]);
        Assert.Equal(ordered.Skip(2), after.Skip(2));

        // Στο πρώτο στοιχείο το «πάνω» δεν κάνει τίποτα
        await RoomTypeOps.MoveAsync(db, after[0], -1);
        Assert.Equal(after, await db.RoomTypes.OrderBy(t => t.SortOrder).ThenBy(t => t.Id).Select(t => t.Id).ToListAsync());
    }

    [Fact]
    public async Task Move_ZeroDirIsNoOp()
    {
        var (db, conn) = TestDb.Create();
        using var _ = conn; using var __ = db;
        await SeedDefaultsAsync(db);
        var before = await db.RoomTypes.OrderBy(t => t.SortOrder).ThenBy(t => t.Id).Select(t => t.Id).ToListAsync();

        await RoomTypeOps.MoveAsync(db, before[1], 0);

        Assert.Equal(before, await db.RoomTypes.OrderBy(t => t.SortOrder).ThenBy(t => t.Id).Select(t => t.Id).ToListAsync());
    }

    [Fact]
    public async Task Save_EmptyColorStoresNull()
    {
        var (db, conn) = TestDb.Create();
        using var _ = conn; using var __ = db;
        await RoomTypeOps.SaveAsync(db, null, "Έγχρωμος", null, "#123456");
        var id = (await db.RoomTypes.SingleAsync()).Id;

        await RoomTypeOps.SaveAsync(db, id, "Έγχρωμος", null, "");

        Assert.Null((await db.RoomTypes.SingleAsync()).Color);
    }
}

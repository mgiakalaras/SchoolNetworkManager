using Xunit;
using SchoolNetworkManager.Models;
using SchoolNetworkManager.Services;

namespace SchoolNetworkManager.Tests;

public class RoomOptionsTests
{
    [Fact]
    public async Task LoadAsync_GroupsByBuildingAndFloorWithUnplacedLast()
    {
        var (db, conn) = TestDb.Create();
        using var _ = conn; using var __ = db;

        var building = new Building { Name = "Κτίριο 1", SortOrder = 0 };
        db.Buildings.Add(building);
        await db.SaveChangesAsync();

        // Ο 1ος όροφος μπαίνει πρώτος στη βάση, για να φανεί ότι η σειρά ακολουθεί το Level.
        var first = new Floor { BuildingId = building.Id, Name = "1ος", Level = 1 };
        var ground = new Floor { BuildingId = building.Id, Name = "Ισόγειο", Level = 0 };
        db.Floors.AddRange(first, ground);
        await db.SaveChangesAsync();

        db.Rooms.AddRange(
            new Room { Name = "Β", FloorId = ground.Id, SortOrder = 1 },
            new Room { Name = "Α", FloorId = ground.Id, SortOrder = 1 },
            new Room { Name = "Γ", FloorId = ground.Id, SortOrder = 0 },
            new Room { Name = "Δ", FloorId = first.Id },
            new Room { Name = "Αταξινόμητο" });
        await db.SaveChangesAsync();

        var options = await RoomOptions.LoadAsync(db);

        Assert.Equal(
            new[] { "Κτίριο 1 › Ισόγειο", "Κτίριο 1 › Ισόγειο", "Κτίριο 1 › Ισόγειο", "Κτίριο 1 › 1ος", "Αταξινόμητοι χώροι" },
            options.Select(o => o.Group).ToArray());
        Assert.Equal(new[] { "Γ", "Α", "Β", "Δ", "Αταξινόμητο" }, options.Select(o => o.Name).ToArray());
    }
}

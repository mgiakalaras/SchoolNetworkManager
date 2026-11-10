using Xunit;
using SchoolNetworkManager.Models;
using SchoolNetworkManager.Services;

namespace SchoolNetworkManager.Tests;

public class RoomGroupingTests
{
    private static readonly Building[] Buildings = { new() { Id = 1, Name = "Κτίριο 1" } };

    [Fact]
    public void Group_NoRoomHasFloor_ReturnsNull()
    {
        var rooms = new[] { new Room { Id = 1, Name = "Α" } };
        var floors = new[] { new Floor { Id = 1, BuildingId = 1, Name = "Ισόγειο" } };

        Assert.Null(RoomGrouping.Group(rooms, floors, Buildings, new[] { "Α" }));
    }

    [Fact]
    public void Group_OrdersFloorsByLevel_UnplacedLast()
    {
        var floors = new[]
        {
            new Floor { Id = 1, BuildingId = 1, Name = "1ος", Level = 1 },
            new Floor { Id = 2, BuildingId = 1, Name = "Ισόγειο", Level = 0 },
            new Floor { Id = 3, BuildingId = 1, Name = "Υπόγειο", Level = -1 }
        };
        var rooms = new[]
        {
            new Room { Id = 1, Name = "Α", FloorId = 1 },
            new Room { Id = 2, Name = "Β", FloorId = 2 },
            new Room { Id = 3, Name = "Γ", FloorId = null },
            new Room { Id = 4, Name = "Δ", FloorId = 2 }
        };

        var groups = RoomGrouping.Group(rooms, floors, Buildings, new[] { "Α", "Β", "Γ", "Δ", "Άγνωστος" })!;

        Assert.Equal(new[] { "Ισόγειο", "1ος", null }, groups.Select(g => g.Floor));
        Assert.Equal(new[] { "Β", "Δ" }, groups[0].RoomNames);
        Assert.Equal(new[] { "Α" }, groups[1].RoomNames);
        Assert.Equal(new[] { "Γ", "Άγνωστος" }, groups[2].RoomNames);
        Assert.Null(groups[2].Building);
    }

    [Fact]
    public void Group_MatchesNamesIgnoringCaseAndSpaces_AndOrdersBuildings()
    {
        var buildings = new[]
        {
            new Building { Id = 1, Name = "Β", SortOrder = 2 },
            new Building { Id = 2, Name = "Α", SortOrder = 1 }
        };
        var floors = new[]
        {
            new Floor { Id = 1, BuildingId = 1, Name = "Ισόγειο", Level = 0 },
            new Floor { Id = 2, BuildingId = 2, Name = "Ισόγειο", Level = 0 }
        };
        var rooms = new[]
        {
            new Room { Id = 1, Name = "Lab", FloorId = 1 },
            new Room { Id = 2, Name = "Aula", FloorId = 2 }
        };

        var groups = RoomGrouping.Group(rooms, floors, buildings, new[] { " lab ", "AULA" })!;

        Assert.Equal(new[] { "Α", "Β" }, groups.Select(g => g.Building));
    }
}

using Xunit;
using SchoolNetworkManager.Models;
using SchoolNetworkManager.Services;

namespace SchoolNetworkManager.Tests;

public class RoomStatsTests
{
    private const string Pc = "Υπολογιστής";
    private const string Board = "Διαδραστικός Πίνακας";
    private const string Printer = "Εκτυπωτής";

    private static Computer Dev(string type, string name = "dev", bool dynamic = false) =>
        new() { Name = name, MachineType = type, IsDynamic = dynamic };

    private static RoomCapacity Cap(string type, int n) => new() { MachineType = type, PlannedCount = n };

    [Fact]
    public void ForRoom_ExcludesDynamicAndEmptySlots()
    {
        var devices = new[]
        {
            Dev(Pc, "PC1"), Dev(Pc, "PC2"), Dev(Pc, "PC3"),
            Dev(Pc, "dyn", dynamic: true),
            Dev(Pc, "EMPTY 4"),
        };

        var result = RoomStats.ForRoom(devices, new[] { Cap(Pc, 3) });

        var pc = Assert.Single(result);
        Assert.Equal(new TypeCount(Pc, 3, 3), pc);
        Assert.True(pc.Ok);
    }

    [Fact]
    public void ForRoom_ReportsShortOverAndUnplanned()
    {
        var devices = Enumerable.Range(1, 11).Select(i => Dev(Pc, "PC" + i))
            .Append(Dev(Board, "Board")).Append(Dev(Printer, "Printer")).ToList();
        var plan = new[] { Cap(Pc, 12), Cap(Board, 1) };

        var result = RoomStats.ForRoom(devices, plan);

        Assert.Equal(3, result.Count);
        Assert.Equal(new TypeCount(Pc, 12, 11), result[0]);
        Assert.False(result[0].Ok);
        Assert.Equal(new TypeCount(Board, 1, 1), result[1]);
        Assert.True(result[1].Ok);
        Assert.Equal(new TypeCount(Printer, 0, 1), result[2]);
        Assert.True(result[2].Unplanned);
        Assert.False(result[2].Ok);
    }

    [Fact]
    public void ForRoom_OmitsZeroPlannedZeroActual_AndUnknownTypesLast()
    {
        var result = RoomStats.ForRoom(new[] { Dev("Zzz") }, new[] { Cap(Printer, 0), Cap(Pc, 1) });

        Assert.Equal(new[] { Pc, "Zzz" }, result.Select(r => r.MachineType));
    }

    [Fact]
    public void Sum_AddsPlannedAndActualPerType()
    {
        var room1 = new[] { new TypeCount(Pc, 12, 11) };
        var room2 = new[] { new TypeCount(Pc, 10, 10), new TypeCount(Printer, 0, 1) };

        var result = RoomStats.Sum(new IReadOnlyList<TypeCount>[] { room1, room2 });

        Assert.Equal(new TypeCount(Pc, 22, 21), result[0]);
        Assert.Equal(new TypeCount(Printer, 0, 1), result[1]);
    }
}

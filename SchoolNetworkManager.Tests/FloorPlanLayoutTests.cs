using Xunit;
using SchoolNetworkManager.Services;

namespace SchoolNetworkManager.Tests;

public class FloorPlanLayoutTests
{
    private static readonly int[] Ids = { 1, 2, 3 };

    [Fact]
    public void Sanitise_ClampsOutOfRangeValues()
    {
        var json = """[{"roomId":1,"x":-10,"y":150,"w":500,"h":-3},{"roomId":2,"x":90,"y":60,"w":40,"h":70}]""";

        var result = FloorPlanLayout.Sanitise(json, Ids);

        Assert.NotNull(result);
        Assert.Equal(new RoomBox(1, 0, 100, 100, 0), result![0]);
        // x+w <= 100 και y+h <= 100
        Assert.Equal(new RoomBox(2, 90, 60, 10, 40), result[1]);
    }

    [Fact]
    public void Sanitise_DropsRoomsOfOtherFloorsAndDuplicates()
    {
        var json = """[{"roomId":1,"x":1,"y":1,"w":10,"h":10},{"roomId":99,"x":5,"y":5,"w":5,"h":5},{"roomId":1,"x":50,"y":50,"w":10,"h":10}]""";

        var result = FloorPlanLayout.Sanitise(json, Ids);

        var box = Assert.Single(result!);
        Assert.Equal(new RoomBox(1, 1, 1, 10, 10), box);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"roomId\":1}")]
    [InlineData("null")]
    [InlineData("[{\"roomId\":\"abc\"}]")]
    public void Sanitise_RejectsBadJson(string? json)
    {
        Assert.Null(FloorPlanLayout.Sanitise(json, Ids));
    }

    [Fact]
    public void Sanitise_EmptyArrayIsValidAndClearsAll()
    {
        var result = FloorPlanLayout.Sanitise("[]", Ids);

        Assert.NotNull(result);
        Assert.Empty(result!);
    }

    [Fact]
    public void Status_NeutralOkWarn()
    {
        Assert.Equal("none", FloorPlanLayout.Status(Array.Empty<TypeCount>()));
        Assert.Equal("ok", FloorPlanLayout.Status(new[] { new TypeCount("Υπολογιστής", 3, 3) }));
        Assert.Equal("warn", FloorPlanLayout.Status(new[] { new TypeCount("Υπολογιστής", 3, 2) }));
        Assert.Equal("warn", FloorPlanLayout.Status(new[] { new TypeCount("Υπολογιστής", 3, 3), new TypeCount("Εκτυπωτής", 0, 1) }));
    }
}

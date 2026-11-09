using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;

namespace SchoolNetworkManager.Services;

/// <summary>Επιλογή χώρου για λίστες επιλογής: Group = «Κτίριο › Όροφος» ή «Αταξινόμητοι χώροι».</summary>
public record RoomOption(int Id, string Name, string Group);

public static class RoomOptions
{
    public const string UnplacedGroup = "Αταξινόμητοι χώροι";

    /// <summary>Όλοι οι χώροι, ταξινομημένοι κατά κτίριο, όροφο, χώρο· οι αταξινόμητοι τελευταίοι.</summary>
    public static async Task<List<RoomOption>> LoadAsync(AppDbContext db)
    {
        var rooms = await db.Rooms.AsNoTracking().ToListAsync();
        var floors = await db.Floors.AsNoTracking().ToDictionaryAsync(f => f.Id);
        var buildings = await db.Buildings.AsNoTracking().ToDictionaryAsync(b => b.Id);

        var placed = rooms
            .Where(r => r.FloorId != null && floors.ContainsKey(r.FloorId.Value) && buildings.ContainsKey(floors[r.FloorId.Value].BuildingId))
            .Select(r =>
            {
                var floor = floors[r.FloorId!.Value];
                var building = buildings[floor.BuildingId];
                return (Room: r, Floor: floor, Building: building);
            })
            .OrderBy(x => x.Building.SortOrder)
            .ThenBy(x => x.Building.Id)
            .ThenBy(x => x.Floor.Level)
            .ThenBy(x => x.Floor.SortOrder)
            .ThenBy(x => x.Floor.Id)
            .ThenBy(x => x.Room.SortOrder)
            .ThenBy(x => x.Room.Name, StringComparer.CurrentCulture)
            .Select(x => new RoomOption(x.Room.Id, x.Room.Name, $"{x.Building.Name} › {x.Floor.Name}"));

        var placedIds = new HashSet<int>();
        var result = new List<RoomOption>();

        foreach (var option in placed)
        {
            placedIds.Add(option.Id);
            result.Add(option);
        }

        result.AddRange(rooms
            .Where(r => !placedIds.Contains(r.Id))
            .OrderBy(r => r.SortOrder)
            .ThenBy(r => r.Name, StringComparer.CurrentCulture)
            .Select(r => new RoomOption(r.Id, r.Name, UnplacedGroup)));

        return result;
    }
}

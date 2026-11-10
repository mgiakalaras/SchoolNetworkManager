using SchoolNetworkManager.Models;

namespace SchoolNetworkManager.Services;

/// <summary>Ομάδα κτίριο › όροφος με τα ονόματα χώρων της. Building/Floor = null στην τελευταία ομάδα (χωρίς όροφο).</summary>
public record RoomGroup(string? Building, string? Floor, List<string> RoomNames);

/// <summary>Ομαδοποίηση ονομάτων χώρων κατά κτίριο › όροφο για την Ετήσια αναφορά και τις εκτυπώσεις.</summary>
public static class RoomGrouping
{
    /// <summary>
    /// Επιστρέφει null όταν κανένας χώρος δεν έχει όροφο (η έξοδος μένει όπως πριν). Αλλιώς ομάδες με σειρά
    /// κτιρίου (SortOrder, Id) και ορόφου (Level, SortOrder, Name)· χώροι χωρίς όροφο και ονόματα που δεν
    /// αντιστοιχούν σε χώρο μπαίνουν στην τελευταία ομάδα. Η σειρά των ονομάτων μέσα στην ομάδα είναι η σειρά εισόδου.
    /// </summary>
    public static List<RoomGroup>? Group(IEnumerable<Room> rooms, IEnumerable<Floor> floors, IEnumerable<Building> buildings, IEnumerable<string> names)
    {
        var roomList = rooms.ToList();
        var floorById = floors.ToDictionary(f => f.Id);

        if (!roomList.Any(r => r.FloorId != null && floorById.ContainsKey(r.FloorId.Value)))
        {
            return null;
        }

        var buildingById = buildings.ToDictionary(b => b.Id);
        var roomByName = new Dictionary<string, Room>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in roomList)
        {
            roomByName.TryAdd(r.Name.Trim(), r);
        }

        var placed = new Dictionary<int, List<string>>();
        var rest = new List<string>();

        foreach (var name in names)
        {
            if (roomByName.TryGetValue(name.Trim(), out var room) && room.FloorId != null && floorById.ContainsKey(room.FloorId.Value))
            {
                if (!placed.TryGetValue(room.FloorId.Value, out var list))
                {
                    placed[room.FloorId.Value] = list = new List<string>();
                }

                list.Add(name);
            }
            else
            {
                rest.Add(name);
            }
        }

        var result = placed.Keys
            .Select(id => floorById[id])
            .OrderBy(f => buildingById.TryGetValue(f.BuildingId, out var b) ? b.SortOrder : int.MaxValue)
            .ThenBy(f => f.BuildingId)
            .ThenBy(f => f.Level).ThenBy(f => f.SortOrder).ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(f => new RoomGroup(buildingById.TryGetValue(f.BuildingId, out var b) ? b.Name : null, f.Name, placed[f.Id]))
            .ToList();

        if (rest.Count > 0)
        {
            result.Add(new RoomGroup(null, null, rest));
        }

        return result;
    }
}

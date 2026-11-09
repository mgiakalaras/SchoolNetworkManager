using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;

namespace SchoolNetworkManager.Services;

/// <summary>Κάρτα χώρου: προγραμματισμένα/πραγματικά ανά τύπο, online και πλήθος συσκευών.</summary>
public record RoomCard(Room Room, RoomType? Type, IReadOnlyList<TypeCount> Counts, int Online, int Devices);

/// <summary>Όροφος με τους χώρους του και τα σύνολά του.</summary>
public record FloorView(Floor Floor, List<RoomCard> Rooms, IReadOnlyList<TypeCount> Totals);

/// <summary>Κτίριο με τους ορόφους του και τα σύνολά του.</summary>
public record BuildingView(Building Building, List<FloorView> Floors, IReadOnlyList<TypeCount> Totals);

/// <summary>Ερωτήματα και κανόνες διαγραφής για τη σελίδα «Κτίρια».</summary>
public static class BuildingQueries
{
    /// <summary>
    /// Φορτώνει κτίρια → ορόφους → χώρους. Online = συσκευές με τελευταία κατάσταση online
    /// (ίδιος κανόνας με τη σελίδα Χώροι). Οι ορόφοι ταξινομούνται κατά Level, SortOrder, Name.
    /// </summary>
    public static async Task<(List<BuildingView> Buildings, List<RoomCard> Unplaced)> LoadAsync(AppDbContext db)
    {
        var buildings = await db.Buildings.AsNoTracking().ToListAsync();
        var floors = await db.Floors.AsNoTracking().ToListAsync();
        var rooms = await db.Rooms.AsNoTracking().ToListAsync();
        var types = (await db.RoomTypes.AsNoTracking().ToListAsync()).ToDictionary(t => t.Id);
        var capacities = (await db.RoomCapacities.AsNoTracking().ToListAsync()).ToLookup(c => c.RoomId);
        var devices = (await db.Computers.AsNoTracking().Where(c => c.RoomId != null).ToListAsync()).ToLookup(c => c.RoomId!.Value);

        RoomCard Card(Room r)
        {
            var devs = devices[r.Id].ToList();
            var type = r.RoomTypeId != null && types.TryGetValue(r.RoomTypeId.Value, out var t) ? t : null;

            return new RoomCard(r, type, RoomStats.ForRoom(devs, capacities[r.Id]),
                devs.Count(c => !c.IsEmptySlot && c.IsOnline == true),
                devs.Count(c => !c.IsEmptySlot));
        }

        var cards = rooms
            .OrderBy(r => r.SortOrder).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(Card).ToList();
        var floorIds = floors.Select(f => f.Id).ToHashSet();

        var result = buildings
            .OrderBy(b => b.SortOrder).ThenBy(b => b.Id)
            .Select(b =>
            {
                var floorViews = floors
                    .Where(f => f.BuildingId == b.Id)
                    .OrderBy(f => f.Level).ThenBy(f => f.SortOrder).ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
                    .Select(f =>
                    {
                        var fr = cards.Where(c => c.Room.FloorId == f.Id).ToList();
                        return new FloorView(f, fr, RoomStats.Sum(fr.Select(c => c.Counts)));
                    })
                    .ToList();

                return new BuildingView(b, floorViews, RoomStats.Sum(floorViews.Select(f => f.Totals)));
            })
            .ToList();

        // Χώροι χωρίς όροφο (ή με όροφο που δεν υπάρχει πια).
        var unplaced = cards.Where(c => c.Room.FloorId == null || !floorIds.Contains(c.Room.FloorId.Value)).ToList();

        return (result, unplaced);
    }

    /// <summary>Διαγράφει όροφο· αρνείται αν έχει χώρους.</summary>
    public static async Task DeleteFloorAsync(AppDbContext db, int floorId)
    {
        var floor = await db.Floors.FindAsync(floorId);

        if (floor == null)
        {
            return;
        }

        var n = await db.Rooms.CountAsync(r => r.FloorId == floorId);

        if (n > 0)
        {
            throw new InvalidOperationException($"Ο όροφος «{floor.Name}» έχει {n} χώροι· μετακίνησέ τους πρώτα.");
        }

        db.Floors.Remove(floor);
        await db.SaveChangesAsync();
    }

    /// <summary>Διαγράφει κτίριο· αρνείται αν έχει χώρους (σε οποιονδήποτε όροφό του). Διαγράφει τους άδειους ορόφους.</summary>
    public static async Task DeleteBuildingAsync(AppDbContext db, int buildingId)
    {
        var building = await db.Buildings.FindAsync(buildingId);

        if (building == null)
        {
            return;
        }

        var floorIds = await db.Floors.Where(f => f.BuildingId == buildingId).Select(f => f.Id).ToListAsync();
        var n = await db.Rooms.CountAsync(r => r.FloorId != null && floorIds.Contains(r.FloorId.Value));

        if (n > 0)
        {
            throw new InvalidOperationException($"Το κτίριο «{building.Name}» έχει {n} χώροι· μετακίνησέ τους πρώτα.");
        }

        db.Floors.RemoveRange(await db.Floors.Where(f => f.BuildingId == buildingId).ToListAsync());
        db.Buildings.Remove(building);
        await db.SaveChangesAsync();
    }
}

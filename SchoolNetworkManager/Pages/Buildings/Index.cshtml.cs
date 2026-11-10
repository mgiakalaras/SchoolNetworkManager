using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;
using SchoolNetworkManager.Services;

namespace SchoolNetworkManager.Pages.Buildings;

public class IndexModel : PageModel
{
    private readonly AppDbContext _context;
    private readonly RoomService _rooms;

    public IndexModel(AppDbContext context, RoomService rooms)
    {
        _context = context;
        _rooms = rooms;
    }

    public List<BuildingView> Buildings { get; set; } = new();

    public List<RoomCard> Unplaced { get; set; } = new();

    public List<RoomType> RoomTypes { get; set; } = new();

    /// <summary>Κτίριο που εμφανίζεται (tab)· προεπιλογή το πρώτο.</summary>
    [BindProperty(SupportsGet = true)]
    public int? Tab { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? StatusKind { get; set; }

    /// <summary>Μετονομασία σε όνομα άλλου χώρου: προσφέρεται «Ένωση» (πηγή → στόχος).</summary>
    [TempData]
    public int? MergeSourceId { get; set; }

    [TempData]
    public int? MergeTargetId { get; set; }

    [TempData]
    public string? MergeTargetName { get; set; }

    /// <summary>Κείμενο επιβεβαίωσης διαγραφής ανά χώρο (με ό,τι άλλο θα διαγραφεί).</summary>
    public Dictionary<int, string> DeleteConfirms { get; set; } = new();

    public async Task OnGetAsync()
    {
        (Buildings, Unplaced) = await BuildingQueries.LoadAsync(_context);
        DeleteConfirms = (await _rooms.DeleteImpactsAsync()).ToDictionary(x => x.Key, x =>
        {
            var impact = x.Value.Describe();
            return impact.Length == 0 ? "Διαγραφή του χώρου;" : $"Διαγραφή του χώρου; {impact}";
        });
        RoomTypes = await _context.RoomTypes.AsNoTracking().OrderBy(t => t.SortOrder).ThenBy(t => t.Id).ToListAsync();

        if (Tab == null || Buildings.All(b => b.Building.Id != Tab))
        {
            Tab = Buildings.FirstOrDefault()?.Building.Id;
        }
    }

    private IActionResult Done(string message, string kind = "success", int? building = null)
    {
        StatusMessage = message;
        StatusKind = kind;

        return RedirectToPage(new { tab = building });
    }

    private static string? CleanText(string? s, int max)
    {
        var t = s?.Trim();

        if (string.IsNullOrEmpty(t))
        {
            return null;
        }

        return t.Length > max ? t[..max] : t;
    }

    // ---------------- Κτίρια ----------------

    public async Task<IActionResult> OnPostCreateBuildingAsync(string? name, string? description)
    {
        var n = CleanText(name, 80);

        if (n == null)
        {
            return Done("Το όνομα του κτιρίου είναι κενό.", "warning");
        }

        var next = await _context.Buildings.AnyAsync() ? await _context.Buildings.MaxAsync(b => b.SortOrder) + 1 : 0;
        var building = new Building { Name = n, Description = CleanText(description, 200), SortOrder = next };
        _context.Buildings.Add(building);
        await _context.SaveChangesAsync();

        return Done($"Προστέθηκε το κτίριο «{n}».", building: building.Id);
    }

    public async Task<IActionResult> OnPostEditBuildingAsync(int id, string? name, string? description)
    {
        var building = await _context.Buildings.FindAsync(id);
        var n = CleanText(name, 80);

        if (building == null || n == null)
        {
            return Done("Το κτίριο δεν βρέθηκε ή το όνομα είναι κενό.", "warning", id);
        }

        building.Name = n;
        building.Description = CleanText(description, 200);
        await _context.SaveChangesAsync();

        return Done($"Αποθηκεύτηκε το κτίριο «{n}».", building: id);
    }

    public async Task<IActionResult> OnPostDeleteBuildingAsync(int id)
    {
        try
        {
            await BuildingQueries.DeleteBuildingAsync(_context, id);
        }
        catch (InvalidOperationException ex)
        {
            return Done(ex.Message, "warning", id);
        }

        return Done("Το κτίριο διαγράφηκε.");
    }

    public async Task<IActionResult> OnPostMoveBuildingAsync(int id, int dir)
    {
        var list = await _context.Buildings.OrderBy(b => b.SortOrder).ThenBy(b => b.Id).ToListAsync();
        Swap(list, list.FindIndex(b => b.Id == id), dir, (b, o) => b.SortOrder = o);
        await _context.SaveChangesAsync();

        return RedirectToPage(new { tab = id });
    }

    // ---------------- Όροφοι ----------------

    public async Task<IActionResult> OnPostCreateFloorAsync(int buildingId, string? name, int level)
    {
        var n = CleanText(name, 80);
        var building = await _context.Buildings.FindAsync(buildingId);

        if (building == null || n == null)
        {
            return Done("Το κτίριο δεν βρέθηκε ή το όνομα του ορόφου είναι κενό.", "warning", buildingId);
        }

        var siblings = await _context.Floors.Where(f => f.BuildingId == buildingId).Select(f => f.SortOrder).ToListAsync();
        _context.Floors.Add(new Floor { BuildingId = buildingId, Name = n, Level = level, SortOrder = siblings.Count == 0 ? 0 : siblings.Max() + 1 });
        await _context.SaveChangesAsync();

        return Done($"Προστέθηκε ο όροφος «{n}».", building: buildingId);
    }

    public async Task<IActionResult> OnPostEditFloorAsync(int id, string? name, int level)
    {
        var floor = await _context.Floors.FindAsync(id);
        var n = CleanText(name, 80);

        if (floor == null || n == null)
        {
            return Done("Ο όροφος δεν βρέθηκε ή το όνομα είναι κενό.", "warning");
        }

        floor.Name = n;
        floor.Level = level;
        await _context.SaveChangesAsync();

        return Done($"Αποθηκεύτηκε ο όροφος «{n}».", building: floor.BuildingId);
    }

    public async Task<IActionResult> OnPostDeleteFloorAsync(int id)
    {
        var building = (await _context.Floors.FindAsync(id))?.BuildingId;

        try
        {
            await BuildingQueries.DeleteFloorAsync(_context, id);
        }
        catch (InvalidOperationException ex)
        {
            return Done(ex.Message, "warning", building);
        }

        return Done("Ο όροφος διαγράφηκε.", building: building);
    }

    public async Task<IActionResult> OnPostMoveFloorAsync(int id, int dir)
    {
        var floor = await _context.Floors.FindAsync(id);

        if (floor == null)
        {
            return RedirectToPage();
        }

        var list = (await _context.Floors.Where(f => f.BuildingId == floor.BuildingId).ToListAsync())
            .OrderBy(f => f.Level).ThenBy(f => f.SortOrder).ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var index = list.FindIndex(f => f.Id == id);
        var neighbour = index + (dir < 0 ? -1 : 1);

        if (neighbour >= 0 && neighbour < list.Count && list[neighbour].Level != floor.Level)
        {
            return Done("Οι όροφοι ταξινομούνται με βάση το επίπεδο — άλλαξε το επίπεδο για να αλλάξει η σειρά.", "warning", floor.BuildingId);
        }

        Swap(list, index, dir, (f, o) => f.SortOrder = o);
        await _context.SaveChangesAsync();

        return RedirectToPage(new { tab = floor.BuildingId });
    }

    /// <summary>Κανονικοποιεί το SortOrder σε 0..n-1 και ανταλλάσσει τη θέση με τον γείτονα (dir -1/+1).</summary>
    private static void Swap<T>(List<T> list, int index, int dir, Action<T, int> setOrder)
    {
        for (var i = 0; i < list.Count; i++)
        {
            setOrder(list[i], i);
        }

        var other = index + (dir < 0 ? -1 : 1);

        if (index < 0 || other < 0 || other >= list.Count)
        {
            return;
        }

        setOrder(list[index], other);
        setOrder(list[other], index);
    }

    // ---------------- Χώροι ----------------

    public async Task<IActionResult> OnPostCreateRoomAsync(string? name, int? floorId, int? roomTypeId, string? description,
        string? autoShutdown, string[]? capType, int[]? capCount)
    {
        var floor = floorId == null ? null : await _context.Floors.FindAsync(floorId);
        var building = floor?.BuildingId;

        if (!TryParseTime(autoShutdown, out var time))
        {
            return Done("Μη έγκυρη ώρα αυτόματου κλεισίματος.", "warning", building);
        }

        var rows = CapacityRows(capType, capCount);

        try
        {
            var room = await _rooms.CreateAsync(name ?? string.Empty, floor?.Id, await ValidTypeAsync(roomTypeId), CleanText(description, 200));

            if (time != null)
            {
                room.AutoShutdown = time;
                await _context.SaveChangesAsync();
            }

            if (rows.Count > 0)
            {
                await _rooms.SetCapacitiesAsync(room.Id, rows);
            }

            return Done($"Προστέθηκε ο χώρος «{room.Name}».", building: building);
        }
        catch (Exception ex) when (ex is RoomNameTakenException or ArgumentException)
        {
            return Done(ex.Message, "warning", building);
        }
    }

    public async Task<IActionResult> OnPostEditRoomAsync(int id, string? name, int? floorId, int? roomTypeId, string? description,
        string? autoShutdown, string[]? capType, int[]? capCount)
    {
        var room = await _context.Rooms.FindAsync(id);

        if (room == null)
        {
            return Done("Ο χώρος δεν βρέθηκε.", "warning");
        }

        var floor = floorId == null ? null : await _context.Floors.FindAsync(floorId);

        if (!TryParseTime(autoShutdown, out var time))
        {
            return Done("Μη έγκυρη ώρα αυτόματου κλεισίματος.", "warning", floor?.BuildingId);
        }

        try
        {
            if (!string.Equals(room.Name, RoomService.NormaliseName(name), StringComparison.Ordinal))
            {
                try
                {
                    await _rooms.RenameAsync(id, name ?? string.Empty);
                }
                catch (RoomNameTakenException)
                {
                    var existing = await _rooms.FindByNameAsync(name);

                    if (existing == null || existing.Id == id)
                    {
                        throw;
                    }

                    MergeSourceId = id;
                    MergeTargetId = existing.Id;
                    MergeTargetName = existing.Name;

                    return Done($"Υπάρχει ήδη χώρος «{existing.Name}» — δεν αποθηκεύτηκε καμία αλλαγή. Αν είναι ο ίδιος χώρος, ένωσέ τους: " +
                                $"οι συσκευές, τα τμήματα και η κάτοψη του «{room.Name}» περνούν στον «{existing.Name}». Αλλιώς διάλεξε άλλο όνομα.",
                        "warning", floor?.BuildingId);
                }
            }

            room = await _context.Rooms.FindAsync(id) ?? room;
            room.FloorId = floor?.Id;
            room.RoomTypeId = await ValidTypeAsync(roomTypeId);
            room.Description = CleanText(description, 200);

            if (room.AutoShutdown != time)
            {
                room.AutoShutdown = time;
                room.AutoShutdownLast = null;
            }

            await _context.SaveChangesAsync();
            await _rooms.SetCapacitiesAsync(id, CapacityRows(capType, capCount));

            return Done($"Αποθηκεύτηκε ο χώρος «{room.Name}».", building: floor?.BuildingId);
        }
        catch (Exception ex) when (ex is RoomNameTakenException or ArgumentException)
        {
            return Done(ex.Message, "warning", floor?.BuildingId);
        }
    }

    public async Task<IActionResult> OnPostDeleteRoomAsync(int id)
    {
        var room = await _context.Rooms.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
        var building = room?.FloorId == null ? null : (await _context.Floors.FindAsync(room.FloorId))?.BuildingId;

        await _rooms.DeleteAsync(id);

        return Done(room == null ? "Ο χώρος δεν βρέθηκε." : $"Ο χώρος «{room.Name}» διαγράφηκε· οι συσκευές του έμειναν χωρίς χώρο.",
            room == null ? "warning" : "success", building);
    }

    /// <summary>Ένωση δύο χώρων (διπλότυπα): όλα περνούν στον targetId, ο sourceId διαγράφεται.</summary>
    public async Task<IActionResult> OnPostMergeRoomAsync(int sourceId, int targetId)
    {
        var source = await _context.Rooms.AsNoTracking().FirstOrDefaultAsync(r => r.Id == sourceId);
        var target = await _context.Rooms.AsNoTracking().FirstOrDefaultAsync(r => r.Id == targetId);

        if (source == null || target == null || sourceId == targetId)
        {
            return Done("Οι χώροι για την ένωση δεν βρέθηκαν.", "warning");
        }

        await _rooms.MergeAsync(sourceId, targetId);

        var floorId = target.FloorId ?? source.FloorId;
        var buildingId = floorId == null ? null : (await _context.Floors.FindAsync(floorId))?.BuildingId;

        return Done($"🔗 Ο χώρος «{source.Name}» ενώθηκε με τον «{target.Name}».", building: buildingId);
    }

    public async Task<IActionResult> OnPostPlaceRoomsAsync(int[]? ids, int floorId)
    {
        var floor = await _context.Floors.FindAsync(floorId);

        if (floor == null || ids == null || ids.Length == 0)
        {
            return Done("Επίλεξε χώρους και όροφο.", "warning");
        }

        var rooms = await _context.Rooms.Where(r => ids.Contains(r.Id)).ToListAsync();

        foreach (var room in rooms)
        {
            room.FloorId = floorId;
        }

        await _context.SaveChangesAsync();

        return Done($"Μετακινήθηκαν {rooms.Count} χώροι στον όροφο «{floor.Name}».", building: floor.BuildingId);
    }

    // ---------------- Βοηθητικά ----------------

    /// <summary>Κενό → null (ok)· αλλιώς TimeOnly → «HH:mm». False αν δεν διαβάζεται.</summary>
    private static bool TryParseTime(string? text, out string? time)
    {
        time = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!TimeOnly.TryParse(text, out var at))
        {
            return false;
        }

        time = at.ToString("HH:mm");

        return true;
    }

    private static List<(string MachineType, int Planned)> CapacityRows(string[]? types, int[]? counts)
    {
        var rows = new List<(string, int)>();

        for (var i = 0; types != null && counts != null && i < Math.Min(types.Length, counts.Length); i++)
        {
            if (!string.IsNullOrWhiteSpace(types[i]) && counts[i] != 0)
            {
                rows.Add((types[i], counts[i]));
            }
        }

        return rows;
    }

    private async Task<int?> ValidTypeAsync(int? roomTypeId) =>
        roomTypeId != null && await _context.RoomTypes.AnyAsync(t => t.Id == roomTypeId) ? roomTypeId : null;
}

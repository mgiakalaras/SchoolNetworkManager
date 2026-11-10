using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;

namespace SchoolNetworkManager.Services;

/// <summary>Το όνομα χώρου υπάρχει ήδη (σύγκριση χωρίς διάκριση πεζών/κεφαλαίων).</summary>
public class RoomNameTakenException(string name) : Exception($"Υπάρχει ήδη χώρος με όνομα «{name}».")
{
    public string RoomName { get; } = name;
}

/// <summary>Τι θα χαθεί/επηρεαστεί αν διαγραφεί ένας χώρος (για το μήνυμα επιβεβαίωσης).</summary>
public record RoomDeleteImpact(int Devices, int Classes, int Seats, bool HasPlan)
{
    /// <summary>Κείμενο μόνο με ό,τι υπάρχει (κενό αν δεν υπάρχει τίποτα επιπλέον).</summary>
    public string Describe()
    {
        var parts = new List<string>();

        if (Classes > 0)
        {
            var classes = Classes == 1 ? "1 τμήμα" : $"{Classes} τμήματα";
            var seats = Seats == 0 ? string.Empty : Seats == 1 ? " (με 1 θέση μαθητή)" : $" (με {Seats} θέσεις μαθητών)";
            parts.Add(classes + seats);
        }

        if (HasPlan)
        {
            parts.Add("η κάτοψη του χώρου");
        }

        var text = parts.Count == 0 ? string.Empty : $"Θα διαγραφούν επίσης: {string.Join(" και ", parts)}.";

        if (Devices > 0)
        {
            var devices = Devices == 1
                ? "Η 1 συσκευή του μένει χωρίς χώρο (δεν διαγράφεται)."
                : $"Οι {Devices} συσκευές του μένουν χωρίς χώρο (δεν διαγράφονται).";
            text = text.Length == 0 ? devices : text + " " + devices;
        }

        return text;
    }
}

/// <summary>
/// Ο μοναδικός «ιδιοκτήτης» της σχέσης συσκευής ↔ χώρου. Το Computer.Location είναι
/// συγχρονισμένο αντίγραφο του ονόματος του χώρου και γράφεται μόνο από εδώ.
/// </summary>
public class RoomService(AppDbContext db)
{
    public const int MaxNameLength = 80;

    /// <summary>
    /// Τιμή της λίστας «Χώρος» για συσκευή χωρίς RoomId που έχει ακόμη το παλιό κείμενο Location
    /// (βάση χωρίς μετατροπή): «κράτα τον παλιό χώρο».
    /// </summary>
    public const int LegacyRoomId = -1;

    /// <summary>
    /// Κόβει τα κενά (και τα πολλαπλά ενδιάμεσα γίνονται ένα), επιστρέφει null αν είναι κενό,
    /// περιορίζει στους 80 χαρακτήρες.
    /// </summary>
    public static string? NormaliseName(string? name)
    {
        var trimmed = name?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        trimmed = string.Join(' ', trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return trimmed.Length > MaxNameLength ? trimmed[..MaxNameLength].TrimEnd() : trimmed;
    }

    /// <summary>
    /// Κλειδί σύγκρισης ονομάτων χώρων: κανονικοποιημένο, χωρίς τόνους/διαλυτικά, κεφαλαία
    /// («ΕΡΓΑΣΤΗΡΙΟ 1» ≡ «Εργαστήριο 1»). Το SQLite δεν μπορεί, οπότε συγκρίνουμε στη μνήμη.
    /// </summary>
    public static string? MatchKey(string? name)
    {
        var normalised = NormaliseName(name);

        if (normalised == null)
        {
            return null;
        }

        var decomposed = normalised.Normalize(System.Text.NormalizationForm.FormD);
        var stripped = new System.Text.StringBuilder(decomposed.Length);

        foreach (var ch in decomposed)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                stripped.Append(ch);
            }
        }

        return stripped.ToString().Normalize(System.Text.NormalizationForm.FormC).ToUpperInvariant();
    }

    /// <summary>Ίδιο όνομα χώρου με τον κανόνα του MatchKey (δύο κενά/null δεν ταιριάζουν).</summary>
    public static bool SameName(string? a, string? b)
    {
        var ka = MatchKey(a);
        return ka != null && string.Equals(ka, MatchKey(b), StringComparison.Ordinal);
    }

    /// <summary>Βρίσκει χώρο με το όνομα (κανονικοποιημένο, χωρίς διάκριση πεζών/κεφαλαίων και τόνων).</summary>
    public async Task<Room?> FindByNameAsync(string? name)
    {
        var normalised = NormaliseName(name);

        if (normalised == null)
        {
            return null;
        }

        // Το SQLite δεν συγκρίνει χωρίς διάκριση πεζών τα μη-ASCII, οπότε το κάνουμε στη μνήμη.
        // Περιλαμβάνονται και χώροι που προστέθηκαν αλλά δεν έχουν αποθηκευτεί ακόμη (Local).
        var rooms = await db.Rooms.ToListAsync();
        rooms.AddRange(db.Rooms.Local.Where(r => r.Id == 0 && !rooms.Contains(r)));

        return rooms
            .OrderBy(r => r.Id == 0 ? int.MaxValue : r.Id)
            .FirstOrDefault(r => SameName(r.Name, normalised));
    }

    public async Task<Room> CreateAsync(string name, int? floorId = null, int? roomTypeId = null, string? description = null)
    {
        var normalised = NormaliseName(name) ?? throw new ArgumentException("Το όνομα του χώρου είναι κενό.", nameof(name));

        if (await FindByNameAsync(normalised) != null)
        {
            throw new RoomNameTakenException(normalised);
        }

        var room = new Room
        {
            Name = normalised,
            FloorId = floorId,
            RoomTypeId = roomTypeId,
            Description = description
        };

        db.Rooms.Add(room);
        await db.SaveChangesAsync();

        return room;
    }

    /// <summary>
    /// Θέτει RoomId + Location (όνομα χώρου ή null). Δεν κάνει SaveChanges.
    /// Με <see cref="LegacyRoomId"/> κρατά τον χώρο του τωρινού c.Location (find-or-create).
    /// </summary>
    public async Task AssignAsync(Computer c, int? roomId)
    {
        if (roomId == LegacyRoomId)
        {
            await AssignByNameAsync(c, c.Location);
            return;
        }

        string? name = null;

        if (roomId != null)
        {
            var room = await db.Rooms.FindAsync(roomId.Value)
                ?? throw new ArgumentException($"Δεν υπάρχει χώρος με Id {roomId}.", nameof(roomId));
            name = room.Name;
        }

        if (c.RoomId != roomId || c.Location != name)
        {
            c.UpdatedAt = DateTime.Now;
        }

        c.RoomId = roomId;
        c.Location = name;
    }

    /// <summary>
    /// Find-or-create (χωρίς όροφο). Επιστρέφει true όταν δημιουργήθηκε νέος χώρος.
    /// ΔΕΝ κάνει ποτέ SaveChanges: ο νέος χώρος μπαίνει στο context και η συσκευή δένεται μέσω navigation.
    /// </summary>
    public async Task<bool> AssignByNameAsync(Computer c, string? name)
    {
        var normalised = NormaliseName(name);

        if (normalised == null)
        {
            await AssignAsync(c, null);
            return false;
        }

        var room = await FindByNameAsync(normalised);
        var created = false;

        if (room == null)
        {
            room = new Room { Name = normalised };
            db.Rooms.Add(room);
            created = true;
        }

        if (c.Location != room.Name || (room.Id != 0 && c.RoomId != room.Id))
        {
            c.UpdatedAt = DateTime.Now;
        }

        // Μέσω navigation: το RoomId συμπληρώνεται στο SaveChanges του καλούντος.
        c.Room = room;
        c.Location = room.Name;

        return created;
    }

    /// <summary>Μετακινεί συσκευές σε χώρο (ή χωρίς χώρο). Αποθηκεύει· επιστρέφει πόσες άλλαξαν.</summary>
    public async Task<int> MoveDevicesAsync(IEnumerable<int> computerIds, int? roomId, string? by)
    {
        var ids = computerIds.Distinct().ToList();
        var computers = await db.Computers.Where(c => ids.Contains(c.Id)).ToListAsync();

        foreach (var computer in computers)
        {
            await AssignAsync(computer, roomId);
            computer.UpdatedAt = DateTime.Now;
            computer.UpdatedBy = by;
        }

        await db.SaveChangesAsync();

        return computers.Count;
    }

    /// <summary>Μετονομάζει χώρο και ό,τι τον αναφέρει με όνομα, σε μία συναλλαγή.</summary>
    public async Task RenameAsync(int roomId, string newName)
    {
        var target = NormaliseName(newName) ?? throw new ArgumentException("Το όνομα του χώρου είναι κενό.", nameof(newName));
        var room = await db.Rooms.FindAsync(roomId) ?? throw new ArgumentException($"Δεν υπάρχει χώρος με Id {roomId}.", nameof(roomId));

        var clash = (await db.Rooms.ToListAsync())
            .Any(r => r.Id != roomId && SameName(r.Name, target));

        if (clash)
        {
            throw new RoomNameTakenException(target);
        }

        var oldName = room.Name;
        var oldNormalised = NormaliseName(oldName);

        await using var tx = await db.Database.BeginTransactionAsync();

        room.Name = target;

        foreach (var computer in await DevicesOfRoomAsync(roomId, oldNormalised))
        {
            computer.RoomId = roomId;
            computer.Location = target;
            computer.UpdatedAt = DateTime.Now;
        }

        await db.SaveChangesAsync();

        // Οι συνδέσεις με όνομα μπορεί να έχουν γραφτεί με άλλη γραφή (πεζά/τόνοι)· φιλτράρουμε στη μνήμη.
        var planItemIds = await IdsLinkedAsync(db.PlanItems.Where(p => p.Location != null).Select(p => new LinkRow(p.Id, p.Location)), oldName);
        var roomPlanIds = await IdsLinkedAsync(db.RoomPlans.Where(p => p.Location != null).Select(p => new LinkRow(p.Id, p.Location)), oldName);
        var classIds = await IdsLinkedAsync(db.SchoolClasses.Where(p => p.Location != null).Select(p => new LinkRow(p.Id, p.Location)), oldName);

        await db.PlanItems.Where(p => planItemIds.Contains(p.Id)).ExecuteUpdateAsync(u => u.SetProperty(p => p.Location, target));
        await db.RoomPlans.Where(p => roomPlanIds.Contains(p.Id)).ExecuteUpdateAsync(u => u.SetProperty(p => p.Location, target));
        await db.SchoolClasses.Where(p => classIds.Contains(p.Id)).ExecuteUpdateAsync(u => u.SetProperty(p => p.Location, target));

        await tx.CommitAsync();
    }

    /// <summary>Διαγράφει χώρο· οι συσκευές μένουν χωρίς χώρο, κάτοψη/τμήματα/θέσεις/δυναμικότητες φεύγουν.</summary>
    public async Task DeleteAsync(int roomId)
    {
        var room = await db.Rooms.FindAsync(roomId);

        if (room == null)
        {
            return;
        }

        var name = room.Name;

        await using var tx = await db.Database.BeginTransactionAsync();

        foreach (var computer in await DevicesOfRoomAsync(roomId, NormaliseName(name)))
        {
            computer.RoomId = null;
            computer.Location = null;
            computer.UpdatedAt = DateTime.Now;
        }

        await db.SaveChangesAsync();

        var classIds = await IdsLinkedAsync(db.SchoolClasses.Where(s => s.Location != null).Select(s => new LinkRow(s.Id, s.Location)), name);
        var planItemIds = await IdsLinkedAsync(db.PlanItems.Where(p => p.Location != null).Select(p => new LinkRow(p.Id, p.Location)), name);
        var roomPlanIds = await IdsLinkedAsync(db.RoomPlans.Where(p => p.Location != null).Select(p => new LinkRow(p.Id, p.Location)), name);

        await db.Seats.Where(s => classIds.Contains(s.SchoolClassId)).ExecuteDeleteAsync();
        await db.SchoolClasses.Where(s => classIds.Contains(s.Id)).ExecuteDeleteAsync();
        await db.PlanItems.Where(p => planItemIds.Contains(p.Id)).ExecuteDeleteAsync();
        await db.RoomPlans.Where(p => roomPlanIds.Contains(p.Id)).ExecuteDeleteAsync();
        await db.RoomCapacities.Where(c => c.RoomId == roomId).ExecuteDeleteAsync();

        db.Rooms.Remove(room);
        await db.SaveChangesAsync();

        await tx.CommitAsync();
    }

    /// <summary>
    /// Επιλογή για τη λίστα «Χώρος» μιας υπάρχουσας συσκευής. Χωρίς RoomId αλλά με κείμενο Location
    /// (βάση χωρίς μετατροπή): ο χώρος με αυτό το όνομα, αλλιώς <see cref="LegacyRoomId"/> και το
    /// όνομα για μια επιπλέον επιλογή «(παλιός χώρος)» — ώστε η αποθήκευση να μη σβήνει τον χώρο.
    /// <paramref name="posted"/>: επανεμφάνιση φόρμας μετά από POST — κρατιέται ό,τι διάλεξε ο χρήστης
    /// (και το «χωρίς χώρο»), και η επιλογή του παλιού χώρου μένει διαθέσιμη.
    /// </summary>
    public async Task<(int? Selected, string? LegacyName)> SelectionAsync(int? roomId, string? location, bool posted = false)
    {
        if (posted)
        {
            var (legacySelection, legacyName) = await SelectionAsync(LegacyRoomId, location);
            return (roomId == LegacyRoomId ? legacySelection : roomId, legacyName);
        }

        if (roomId != null && roomId != LegacyRoomId)
        {
            return (roomId, null);
        }

        var name = NormaliseName(location);

        if (name == null)
        {
            return (null, null);
        }

        var room = await FindByNameAsync(name);

        return room != null ? (room.Id, null) : (LegacyRoomId, name);
    }

    /// <summary>
    /// Ενώνει δύο χώρους (διπλότυπα) σε μία συναλλαγή: συσκευές, κάτοψη, τμήματα (με τις θέσεις τους)
    /// περνούν στον <paramref name="targetRoomId"/>· κρατιούνται η κάτοψη και οι ρυθμίσεις του στόχου
    /// και συμπληρώνονται μόνο όσες λείπουν· ο χώρος-πηγή διαγράφεται.
    /// </summary>
    public async Task MergeAsync(int sourceRoomId, int targetRoomId)
    {
        if (sourceRoomId == targetRoomId)
        {
            throw new ArgumentException("Ο χώρος δεν μπορεί να ενωθεί με τον εαυτό του.", nameof(targetRoomId));
        }

        var source = await db.Rooms.FindAsync(sourceRoomId) ?? throw new ArgumentException($"Δεν υπάρχει χώρος με Id {sourceRoomId}.", nameof(sourceRoomId));
        var target = await db.Rooms.FindAsync(targetRoomId) ?? throw new ArgumentException($"Δεν υπάρχει χώρος με Id {targetRoomId}.", nameof(targetRoomId));
        var sourceName = source.Name;

        await using var tx = await db.Database.BeginTransactionAsync();

        foreach (var computer in await DevicesOfRoomAsync(sourceRoomId, NormaliseName(sourceName)))
        {
            computer.RoomId = target.Id;
            computer.Location = target.Name;
            computer.UpdatedAt = DateTime.Now;
        }

        // Ρυθμίσεις: του στόχου μένουν· από την πηγή μόνο όσες λείπουν
        if (string.IsNullOrWhiteSpace(target.AutoShutdown))
        {
            target.AutoShutdown = source.AutoShutdown;
            target.AutoShutdownLast = source.AutoShutdownLast;
        }

        if (string.IsNullOrWhiteSpace(target.Description))
        {
            target.Description = source.Description;
        }

        if (target.FloorId == null && source.FloorId != null)
        {
            // Η θέση στην κάτοψη ορόφου αφορά τον όροφο της πηγής, οπότε έρχεται μαζί του.
            target.FloorId = source.FloorId;
            target.PlanX = source.PlanX;
            target.PlanY = source.PlanY;
            target.PlanW = source.PlanW;
            target.PlanH = source.PlanH;
        }

        target.RoomTypeId ??= source.RoomTypeId;

        // Δυναμικότητες: του στόχου μένουν· προστίθενται οι τύποι που δεν έχει
        var targetTypes = await db.RoomCapacities.Where(c => c.RoomId == target.Id).Select(c => c.MachineType).ToListAsync();

        foreach (var capacity in await db.RoomCapacities.Where(c => c.RoomId == sourceRoomId).ToListAsync())
        {
            if (targetTypes.Contains(capacity.MachineType))
            {
                db.RoomCapacities.Remove(capacity);
            }
            else
            {
                capacity.RoomId = target.Id;
            }
        }

        await db.SaveChangesAsync();

        var planItemIds = await IdsLinkedAsync(db.PlanItems.Where(p => p.Location != null).Select(p => new LinkRow(p.Id, p.Location)), sourceName);
        var classIds = await IdsLinkedAsync(db.SchoolClasses.Where(s => s.Location != null).Select(s => new LinkRow(s.Id, s.Location)), sourceName);
        var sourcePlanIds = await IdsLinkedAsync(db.RoomPlans.Where(p => p.Location != null).Select(p => new LinkRow(p.Id, p.Location)), sourceName);
        var targetHasPlan = (await IdsLinkedAsync(db.RoomPlans.Where(p => p.Location != null).Select(p => new LinkRow(p.Id, p.Location)), target.Name)).Count > 0;

        await db.PlanItems.Where(p => planItemIds.Contains(p.Id)).ExecuteUpdateAsync(u => u.SetProperty(p => p.Location, target.Name));
        await db.SchoolClasses.Where(s => classIds.Contains(s.Id)).ExecuteUpdateAsync(u => u.SetProperty(s => s.Location, target.Name));

        if (targetHasPlan)
        {
            await db.RoomPlans.Where(p => sourcePlanIds.Contains(p.Id)).ExecuteDeleteAsync();
        }
        else
        {
            await db.RoomPlans.Where(p => sourcePlanIds.Contains(p.Id)).ExecuteUpdateAsync(u => u.SetProperty(p => p.Location, target.Name));
        }

        db.Rooms.Remove(source);
        await db.SaveChangesAsync();

        await tx.CommitAsync();
    }

    /// <summary>Τι θα διαγράψει το <see cref="DeleteAsync"/> (ίδια σύνδεση με όνομα).</summary>
    public async Task<RoomDeleteImpact> DeleteImpactAsync(int roomId)
    {
        return (await DeleteImpactsAsync()).TryGetValue(roomId, out var impact) ? impact : new RoomDeleteImpact(0, 0, 0, false);
    }

    /// <summary>Το <see cref="DeleteImpactAsync"/> για όλους τους χώρους μαζί (λίγα ερωτήματα, για λίστες).</summary>
    public async Task<Dictionary<int, RoomDeleteImpact>> DeleteImpactsAsync()
    {
        var rooms = await db.Rooms.AsNoTracking().ToListAsync();
        var computers = await db.Computers.AsNoTracking()
            .Where(c => c.RoomId != null || c.Location != null)
            .Select(c => new { c.RoomId, c.Location })
            .ToListAsync();
        var classes = await db.SchoolClasses.AsNoTracking().Where(s => s.Location != null).Select(s => new LinkRow(s.Id, s.Location)).ToListAsync();
        var seatsByClass = (await db.Seats.AsNoTracking().GroupBy(s => s.SchoolClassId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync())
            .ToDictionary(x => x.Key, x => x.Count);
        var planNames = (await db.RoomPlans.AsNoTracking().Where(p => p.Location != null).Select(p => p.Location).ToListAsync())
            .Concat(await db.PlanItems.AsNoTracking().Where(p => p.Location != null).Select(p => p.Location).ToListAsync())
            .Select(MatchKey)
            .Where(k => k != null)
            .ToHashSet(StringComparer.Ordinal);

        var result = new Dictionary<int, RoomDeleteImpact>();

        foreach (var room in rooms)
        {
            var devices = computers.Count(c => c.RoomId == room.Id || (c.RoomId == null && SameName(c.Location, room.Name)));
            var classIds = classes.Where(s => SameName(s.Location, room.Name)).Select(s => s.Id).ToList();
            var seats = classIds.Sum(id => seatsByClass.GetValueOrDefault(id));
            var key = MatchKey(room.Name);

            result[room.Id] = new RoomDeleteImpact(devices, classIds.Count, seats, key != null && planNames.Contains(key));
        }

        return result;
    }

    /// <summary>Αντικαθιστά τις προγραμματισμένες δυναμικότητες του χώρου (το 0 αγνοείται).</summary>
    public async Task SetCapacitiesAsync(int roomId, IEnumerable<(string MachineType, int Planned)> rows)
    {
        var list = rows.ToList();

        foreach (var (type, planned) in list)
        {
            if (!MachineTypes.All.Contains(type))
            {
                throw new ArgumentException($"Άγνωστος τύπος συσκευής «{type}».", nameof(rows));
            }

            if (planned < 0)
            {
                throw new ArgumentException($"Αρνητικός αριθμός για «{type}».", nameof(rows));
            }
        }

        var existing = await db.RoomCapacities.Where(c => c.RoomId == roomId).ToListAsync();
        db.RoomCapacities.RemoveRange(existing);

        // Αν ο ίδιος τύπος εμφανίζεται δύο φορές, αθροίζεται.
        foreach (var group in list.Where(r => r.Planned > 0).GroupBy(r => r.MachineType))
        {
            db.RoomCapacities.Add(new RoomCapacity { RoomId = roomId, MachineType = group.Key, PlannedCount = group.Sum(r => r.Planned) });
        }

        await db.SaveChangesAsync();
    }

    private record LinkRow(int Id, string? Location);

    /// <summary>Id των εγγραφών που συνδέονται με το όνομα (ίδια σύγκριση με το SameName).</summary>
    private static async Task<List<int>> IdsLinkedAsync(IQueryable<LinkRow> rows, string name)
    {
        return (await rows.ToListAsync()).Where(r => SameName(r.Location, name)).Select(r => r.Id).ToList();
    }

    /// <summary>Συσκευές του χώρου: με RoomId, ή (δεδομένα χωρίς μετατροπή) χωρίς RoomId και Location ίσο με το όνομα.</summary>
    private async Task<List<Computer>> DevicesOfRoomAsync(int roomId, string? normalisedName)
    {
        var candidates = await db.Computers.Where(c => c.RoomId == roomId || (c.RoomId == null && c.Location != null)).ToListAsync();

        return candidates
            .Where(c => c.RoomId == roomId || SameName(c.Location, normalisedName))
            .ToList();
    }
}

using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;

namespace SchoolNetworkManager.Services;

/// <summary>Το όνομα χώρου υπάρχει ήδη (σύγκριση χωρίς διάκριση πεζών/κεφαλαίων).</summary>
public class RoomNameTakenException(string name) : Exception($"Υπάρχει ήδη χώρος με όνομα «{name}».")
{
    public string RoomName { get; } = name;
}

/// <summary>
/// Ο μοναδικός «ιδιοκτήτης» της σχέσης συσκευής ↔ χώρου. Το Computer.Location είναι
/// συγχρονισμένο αντίγραφο του ονόματος του χώρου και γράφεται μόνο από εδώ.
/// </summary>
public class RoomService(AppDbContext db)
{
    public const int MaxNameLength = 80;

    /// <summary>Κόβει τα κενά, επιστρέφει null αν είναι κενό, περιορίζει στους 80 χαρακτήρες.</summary>
    public static string? NormaliseName(string? name)
    {
        var trimmed = name?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length > MaxNameLength ? trimmed[..MaxNameLength].TrimEnd() : trimmed;
    }

    /// <summary>Βρίσκει χώρο με το όνομα (κανονικοποιημένο, χωρίς διάκριση πεζών/κεφαλαίων).</summary>
    public async Task<Room?> FindByNameAsync(string? name)
    {
        var normalised = NormaliseName(name);

        if (normalised == null)
        {
            return null;
        }

        // Το SQLite δεν συγκρίνει χωρίς διάκριση πεζών τα μη-ASCII, οπότε το κάνουμε στη μνήμη.
        var rooms = await db.Rooms.ToListAsync();

        return rooms
            .OrderBy(r => r.Id)
            .FirstOrDefault(r => string.Equals(NormaliseName(r.Name), normalised, StringComparison.OrdinalIgnoreCase));
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

    /// <summary>Θέτει RoomId + Location (όνομα χώρου ή null). Δεν κάνει SaveChanges.</summary>
    public async Task AssignAsync(Computer c, int? roomId)
    {
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

    /// <summary>Find-or-create (χωρίς όροφο). Επιστρέφει true όταν δημιουργήθηκε νέος χώρος. Δεν κάνει SaveChanges.</summary>
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
            // Χρειαζόμαστε το Id· το αποθηκεύουμε μαζί με τον νέο χώρο.
            await db.SaveChangesAsync();
            created = true;
        }

        await AssignAsync(c, room.Id);

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
            .Any(r => r.Id != roomId && string.Equals(NormaliseName(r.Name), target, StringComparison.OrdinalIgnoreCase));

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

        await db.PlanItems.Where(p => p.Location == oldName).ExecuteUpdateAsync(u => u.SetProperty(p => p.Location, target));
        await db.RoomPlans.Where(p => p.Location == oldName).ExecuteUpdateAsync(u => u.SetProperty(p => p.Location, target));
        await db.SchoolClasses.Where(p => p.Location == oldName).ExecuteUpdateAsync(u => u.SetProperty(p => p.Location, target));

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

        var classIds = await db.SchoolClasses.Where(s => s.Location == name).Select(s => s.Id).ToListAsync();

        await db.Seats.Where(s => classIds.Contains(s.SchoolClassId)).ExecuteDeleteAsync();
        await db.SchoolClasses.Where(s => s.Location == name).ExecuteDeleteAsync();
        await db.PlanItems.Where(p => p.Location == name).ExecuteDeleteAsync();
        await db.RoomPlans.Where(p => p.Location == name).ExecuteDeleteAsync();
        await db.RoomCapacities.Where(c => c.RoomId == roomId).ExecuteDeleteAsync();

        db.Rooms.Remove(room);
        await db.SaveChangesAsync();

        await tx.CommitAsync();
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

    /// <summary>Συσκευές του χώρου: με RoomId, ή (δεδομένα χωρίς μετατροπή) χωρίς RoomId και Location ίσο με το όνομα.</summary>
    private async Task<List<Computer>> DevicesOfRoomAsync(int roomId, string? normalisedName)
    {
        var candidates = await db.Computers.Where(c => c.RoomId == roomId || (c.RoomId == null && c.Location != null)).ToListAsync();

        return candidates
            .Where(c => c.RoomId == roomId
                || (normalisedName != null && string.Equals(NormaliseName(c.Location), normalisedName, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }
}

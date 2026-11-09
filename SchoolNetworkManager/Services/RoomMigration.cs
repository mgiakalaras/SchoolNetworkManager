using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;

namespace SchoolNetworkManager.Services;

/// <summary>
/// Εφάπαξ μετατροπή των υπαρχόντων χώρων: οι συσκευές που είχαν μόνο κείμενο Location
/// συνδέονται πλέον με εγγραφή Room (RoomId). Τρέχει στην εκκίνηση, μία φορά ανά βάση
/// (φρουρός: AppSettings.RoomsMigratedAt), αφού πρώτα γίνει αντίγραφο ασφαλείας,
/// και μέσα σε μία συναλλαγή — αν κάτι αποτύχει, η βάση μένει όπως ήταν.
/// </summary>
public static class RoomMigration
{
    public const string DefaultBuildingName = "Κτίριο 1";
    public const string DefaultFloorName = "Χωρίς όροφο";

    /// <summary>
    /// Επιστρέφει true αν έκανε τη μετατροπή σε αυτή την εκτέλεση.
    /// <paramref name="afterRoomsCreated"/> είναι σημείο ελέγχου για τα τεστ: καλείται μέσα στη
    /// συναλλαγή, αφού δημιουργηθούν οι χώροι και πριν συνδεθούν οι συσκευές.
    /// </summary>
    public static async Task<bool> RunAsync(AppDbContext db, Func<Task> backup, ILogger log, Func<Task>? afterRoomsCreated = null)
    {
        var settings = await db.AppSettings.FirstOrDefaultAsync(s => s.Id == 1);

        if (settings == null)
        {
            log.LogWarning("Δεν υπάρχουν ρυθμίσεις (AppSettings)· η μετατροπή των χώρων αναβάλλεται.");
            return false;
        }

        if (settings.RoomsMigratedAt != null)
        {
            return false;
        }

        // Πρώτα αντίγραφο ασφαλείας· χωρίς αυτό δεν αγγίζουμε τα δεδομένα (θα ξαναδοκιμάσει στην επόμενη εκκίνηση).
        try
        {
            await backup();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Το αντίγραφο ασφαλείας πριν τη μετατροπή των χώρων απέτυχε· η μετατροπή παραλείπεται.");
            return false;
        }

        await using var tx = await db.Database.BeginTransactionAsync();

        try
        {
            // 1. Προεπιλεγμένο κτίριο και όροφος
            if (!await db.Buildings.AnyAsync())
            {
                var building = new Building { Name = DefaultBuildingName };
                db.Buildings.Add(building);
                await db.SaveChangesAsync();

                db.Floors.Add(new Floor { BuildingId = building.Id, Name = DefaultFloorName, Level = 0 });
                await db.SaveChangesAsync();
            }

            // 2. Προεπιλεγμένοι τύποι χώρων
            if (!await db.RoomTypes.AnyAsync())
            {
                var order = 0;

                foreach (var (name, icon) in RoomTypeDefaults.All)
                {
                    db.RoomTypes.Add(new RoomType { Name = name, Icon = icon, SortOrder = order++ });
                }

                await db.SaveChangesAsync();
            }

            // 3. Συγχώνευση χώρων που συγκρούονται μετά την κανονικοποίηση (μένει το μικρότερο Id)
            var byName = new Dictionary<string, Room>(StringComparer.OrdinalIgnoreCase);
            var removed = new Dictionary<int, Room>();

            foreach (var room in await db.Rooms.OrderBy(r => r.Id).ToListAsync())
            {
                var key = RoomService.NormaliseName(room.Name);

                if (key == null)
                {
                    continue;
                }

                if (byName.TryGetValue(key, out var keeper))
                {
                    // Να μη χαθεί ρύθμιση που υπήρχε μόνο στο διπλότυπο
                    keeper.AutoShutdown ??= room.AutoShutdown;
                    keeper.Description ??= room.Description;
                    removed[room.Id] = keeper;
                    db.Rooms.Remove(room);
                }
                else
                {
                    room.Name = key;
                    byName[key] = room;
                }
            }

            var computers = await db.Computers.OrderBy(c => c.Id).ToListAsync();

            foreach (var computer in computers.Where(c => c.RoomId != null && removed.ContainsKey(c.RoomId.Value)))
            {
                computer.RoomId = removed[computer.RoomId!.Value].Id;
            }

            // 4. Χώροι για κάθε όνομα που υπάρχει μόνο στις συσκευές.
            //    Όνομα: η πιο συχνή παραλλαγή (ισοπαλία → της συσκευής με το μικρότερο Id).
            var missing = computers
                .Select(c => (Computer: c, Name: RoomService.NormaliseName(c.Location)))
                .Where(x => x.Name != null && !byName.ContainsKey(x.Name))
                .GroupBy(x => x.Name!, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.GroupBy(x => x.Name!, StringComparer.Ordinal)
                    .OrderByDescending(v => v.Count())
                    .ThenBy(v => v.Min(x => x.Computer.Id))
                    .First()
                    .Key)
                .ToList();

            var created = missing.Select(name => new Room { Name = name }).ToList();
            db.Rooms.AddRange(created);
            await db.SaveChangesAsync();

            foreach (var room in created)
            {
                byName[room.Name] = room;
            }

            if (afterRoomsCreated != null)
            {
                await afterRoomsCreated();
            }

            // 5. Σύνδεση συσκευών· το Location γίνεται ακριβώς το όνομα του χώρου
            foreach (var computer in computers)
            {
                var key = RoomService.NormaliseName(computer.Location);

                if (key == null)
                {
                    computer.RoomId = null;
                    computer.Location = null;
                }
                else
                {
                    var room = byName[key];
                    computer.RoomId = room.Id;
                    computer.Location = room.Name;
                }
            }

            // 6. Τα κείμενα-σύνδεσμοι άλλων πινάκων (κάτοψη, τμήματα) δείχνουν στο ίδιο όνομα
            foreach (var item in await db.PlanItems.ToListAsync())
            {
                item.Location = Canonical(byName, item.Location);
            }

            foreach (var plan in await db.RoomPlans.ToListAsync())
            {
                plan.Location = Canonical(byName, plan.Location);
            }

            foreach (var schoolClass in await db.SchoolClasses.ToListAsync())
            {
                schoolClass.Location = Canonical(byName, schoolClass.Location);
            }

            settings.RoomsMigratedAt = DateTime.Now;
            await db.SaveChangesAsync();
            await tx.CommitAsync();

            log.LogInformation("Μετατροπή χώρων ολοκληρώθηκε: {Rooms} χώροι, {Devices} συσκευές.", byName.Count, computers.Count(c => c.RoomId != null));
            return true;
        }
        catch (Exception ex)
        {
            try
            {
                await tx.RollbackAsync();
            }
            catch (Exception rollbackEx)
            {
                log.LogError(rollbackEx, "Αποτυχία ακύρωσης της συναλλαγής μετατροπής χώρων.");
            }

            // Να μη μείνουν στο context αλλαγές που δεν αποθηκεύτηκαν
            db.ChangeTracker.Clear();
            log.LogError(ex, "Η μετατροπή των χώρων απέτυχε· η βάση έμεινε όπως ήταν και θα ξαναδοκιμαστεί στην επόμενη εκκίνηση.");
            return false;
        }
    }

    /// <summary>Το όνομα του χώρου αν το κείμενο ταιριάζει σε κάποιον (μετά την κανονικοποίηση), αλλιώς όπως ήταν.</summary>
    private static string Canonical(Dictionary<string, Room> rooms, string location)
    {
        var key = RoomService.NormaliseName(location);
        return key != null && rooms.TryGetValue(key, out var room) ? room.Name : location;
    }
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;
using SchoolNetworkManager.Services;

namespace SchoolNetworkManager.Pages.Locations;

public class IndexModel : PageModel
{
    public const string NoLocationKey = "__none__";
    public const string NoLocationName = "Χωρίς χώρο";

    private readonly AppDbContext _context;
    private readonly SettingsService _settings;

    public IndexModel(AppDbContext context, SettingsService settings)
    {
        _context = context;
        _settings = settings;
    }

    public List<LocationGroup> Groups { get; set; } = new();

    /// <summary>Συσκευές με ανοιχτή βλάβη (true = εκτός λειτουργίας).</summary>
    public Dictionary<int, bool> OpenFaults { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? StatusKind { get; set; }

    public int DynamicCount { get; set; }

    /// <summary>Εμφάνιση ενεργειών ρεύματος (ρύθμιση).</summary>
    public bool ShowPower { get; set; } = true;

    /// <summary>Όλες οι συσκευές (και dynamic), για το «＋ Προσθήκη συσκευών».</summary>
    public List<Computer> AllDevices { get; set; } = new();

    /// <summary>Ανοίγει αμέσως το παράθυρο προσθήκης για αυτόν τον χώρο (μετά τη δημιουργία του).</summary>
    [BindProperty(SupportsGet = true)]
    public string? Add { get; set; }

    public async Task OnGetAsync()
    {
        ShowPower = _settings.Current.ScreenWall;
                var everything = await _context.Computers.AsNoTracking().ToListAsync();
        OpenFaults = await FaultLookup.OpenByComputerAsync(_context);

        // Οι dynamic διευθύνσεις δεν ανήκουν σε χώρο — δεν εμφανίζονται εδώ.
        DynamicCount = everything.Count(c => c.IsDynamic);
        var computers = everything.Where(c => !c.IsDynamic).ToList();

        AllDevices = everything.OrderBy(c => NetworkTools.IpToNumber(c.IpAddress)).ToList();
        var rooms = await _context.Rooms.AsNoTracking().ToListAsync();

        Groups = computers
            .GroupBy(c => string.IsNullOrWhiteSpace(c.Location) ? NoLocationName : c.Location.Trim(),
                     StringComparer.OrdinalIgnoreCase)
            .Select(g => new LocationGroup
            {
                Name = g.Key,
                Key = g.Key == NoLocationName ? NoLocationKey : g.Key,
                Computers = g.OrderBy(c => NetworkTools.IpToNumber(c.IpAddress)).ToList()
            })
            .ToList();

        // Χώροι που δημιουργήθηκαν αλλά δεν έχουν ακόμη συσκευές
        foreach (var room in rooms.Where(r => !Groups.Any(g => string.Equals(g.Name, r.Name, StringComparison.OrdinalIgnoreCase))))
        {
            Groups.Add(new LocationGroup { Name = room.Name, Key = room.Name, Description = room.Description });
        }

        var agentIds = (await _context.AgentReports.Select(r => r.ComputerId).ToListAsync()).ToHashSet();

        foreach (var group in Groups)
        {
            var room = rooms.FirstOrDefault(r => string.Equals(r.Name, group.Name, StringComparison.OrdinalIgnoreCase));
            group.Description ??= room?.Description;
            group.AutoShutdown = room?.AutoShutdown;
            group.WithAgent = group.Computers.Count(c => !c.IsEmptySlot && agentIds.Contains(c.Id));
        }

        Groups = Groups
            .OrderBy(g => g.Key == NoLocationKey)
            .ThenBy(g => g.Name)
            .ToList();
    }

    public async Task<IActionResult> OnPostCheckAsync(string? location)
    {
        var computers = await LoadLocationAsync(location);
        var (online, offline) = await StatusChecker.CheckAsync(_context, computers);
        await _context.SaveChangesAsync();

        StatusKind = "success";
        StatusMessage = $"{DisplayName(location)}: Online {online}, Offline {offline}.";

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostWakeAsync(string? location)
    {
        var computers = (await LoadLocationAsync(location))
            .Where(c => !c.IsEmptySlot)
            .ToList();

        var sent = 0;

        foreach (var computer in computers.Where(c => !string.IsNullOrWhiteSpace(c.MacAddress)))
        {
            if (await NetworkTools.SendWakeOnLanAsync(computer.MacAddress, _settings.Current.WolBroadcast))
            {
                sent++;
            }
        }

        var missing = computers.Count(c => string.IsNullOrWhiteSpace(c.MacAddress));

        StatusKind = sent > 0 ? "success" : "warning";
        StatusMessage = sent > 0
            ? $"{DisplayName(location)}: στάλθηκε σήμα αφύπνισης σε {sent} συσκευές." +
              (missing > 0 ? $" {missing} χωρίς MAC δεν μπορούν να ανάψουν." : string.Empty) +
              " Κάνε «Έλεγχο» σε 1-2 λεπτά."
            : $"{DisplayName(location)}: καμία συσκευή δεν έχει MAC Address.";

        return RedirectToPage();
    }

    // Κλείσιμο / επανεκκίνηση όλων των υπολογιστών ενός χώρου (όσων έχουν agent).
    public async Task<IActionResult> OnPostPowerAsync(string? location, string? kind)
    {
        if (!AgentCommands.IsValidKind(kind) || kind is not ("shutdown" or "restart"))
        {
            StatusKind = "warning";
            StatusMessage = "Άγνωστη ενέργεια.";
            return RedirectToPage();
        }

        var computers = (await LoadLocationAsync(location)).Where(c => !c.IsEmptySlot).ToList();
        var ids = computers.Select(c => c.Id).ToList();

        var withAgent = await _context.AgentReports
            .Where(r => ids.Contains(r.ComputerId))
            .Select(r => r.ComputerId)
            .ToListAsync();

        var by = UserPasswords.ActorName(User);
        var sent = 0;

        foreach (var pc in computers.Where(c => withAgent.Contains(c.Id)))
        {
            var verb = kind == "restart" ? "επανεκκινήσει" : "κλείσει";
            await AgentCommands.QueueAsync(_context, pc.Id, kind!, by, delaySeconds: 60,
                text: $"Το εργαστήριο κλείνει. Ο υπολογιστής θα {verb} σε 1 λεπτό.");
            sent++;
        }

        var noAgent = computers.Count - sent;
        StatusKind = sent > 0 ? "success" : "warning";
        var label = kind == "restart" ? "Επανεκκίνηση" : "Κλείσιμο";
        StatusMessage = sent > 0
            ? $"{DisplayName(location)}: {label.ToLower()} σε {sent} υπολογιστές (σε 1 λεπτό)." +
              (noAgent > 0 ? $" {noAgent} χωρίς agent δεν δέχονται εντολές." : string.Empty)
            : $"{DisplayName(location)}: κανένας υπολογιστής δεν έχει agent για να δεχτεί την εντολή.";
        return RedirectToPage();
    }

    // Ώρα αυτόματου βραδινού κλεισίματος ανά χώρο («» = καμία).
    public async Task<IActionResult> OnPostAutoShutdownAsync(string? location, string? time)
    {
        var name = location?.Trim();

        if (string.IsNullOrWhiteSpace(name) || name == NoLocationKey)
        {
            return RedirectToPage();
        }

        var room = await _context.Rooms.FirstOrDefaultAsync(r => r.Name == name);

        if (room == null)
        {
            room = new Room { Name = name, CreatedAt = DateTime.Now };
            _context.Rooms.Add(room);
        }

        if (string.IsNullOrWhiteSpace(time))
        {
            room.AutoShutdown = null;
            StatusMessage = $"{DisplayName(location)}: το αυτόματο βραδινό κλείσιμο απενεργοποιήθηκε.";
        }
        else if (TimeOnly.TryParse(time, out var at))
        {
            room.AutoShutdown = at.ToString("HH:mm");
            room.AutoShutdownLast = null;
            StatusMessage = $"🌙 {DisplayName(location)}: οι υπολογιστές θα κλείνουν αυτόματα κάθε μέρα στις {room.AutoShutdown} (όσοι είναι ανοιχτοί και έχουν agent).";
        }
        else
        {
            StatusKind = "warning";
            StatusMessage = "Μη έγκυρη ώρα.";
            return RedirectToPage();
        }

        await _context.SaveChangesAsync();
        StatusKind ??= "success";
        return RedirectToPage();
    }

    /// <summary>Αρχείο εισαγωγής για Veyon (όλοι οι χώροι ή ένας).</summary>
    public async Task<IActionResult> OnGetVeyonAsync(string? location)
    {
        var computers = await _context.Computers.AsNoTracking().ToListAsync();

        if (!string.IsNullOrWhiteSpace(location))
        {
            computers = computers.Where(c => MatchesLocation(c, location)).ToList();
        }

        var zip = VeyonExport.BuildZip(computers, _settings.Current.SchoolTitle);
        var suffix = string.IsNullOrWhiteSpace(location) ? "all" : "room";

        return File(zip, "application/zip", $"veyon-import-{suffix}-{DateTime.Now:yyyy-MM-dd}.zip");
    }

    /// <summary>Νέος (άδειος) χώρος· μετά ανοίγει η προσθήκη συσκευών.</summary>
    public async Task<IActionResult> OnPostCreateRoomAsync(string? name, string? description)
    {
        var roomName = name?.Trim();

        if (string.IsNullOrWhiteSpace(roomName) || roomName == NoLocationKey || roomName.Equals(NoLocationName, StringComparison.OrdinalIgnoreCase))
        {
            StatusKind = "warning";
            StatusMessage = "Γράψε όνομα χώρου.";
            return RedirectToPage();
        }

        if (roomName.Length > 80)
        {
            roomName = roomName[..80];
        }

        var exists = (await _context.Rooms.Select(r => r.Name).ToListAsync())
                     .Any(n => string.Equals(n, roomName, StringComparison.OrdinalIgnoreCase)) ||
                     (await _context.Computers.Where(c => c.Location != null).Select(c => c.Location!).Distinct().ToListAsync())
                     .Any(n => string.Equals(n.Trim(), roomName, StringComparison.OrdinalIgnoreCase));

        if (!exists)
        {
            _context.Rooms.Add(new Room
            {
                Name = roomName,
                Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                CreatedAt = DateTime.Now
            });
            await _context.SaveChangesAsync();
        }

        StatusKind = "success";
        StatusMessage = exists
            ? $"Ο χώρος «{roomName}» υπάρχει ήδη — πρόσθεσε συσκευές σε αυτόν."
            : $"🏫 Ο χώρος «{roomName}» δημιουργήθηκε. Διάλεξε τις συσκευές που ανήκουν σε αυτόν.";

        return RedirectToPage(new { Add = roomName });
    }

    /// <summary>Προσθήκη συσκευών σε χώρο (οι dynamic γίνονται σταθερές συσκευές).</summary>
    public async Task<IActionResult> OnPostAddToRoomAsync(string? room, int[] ids)
    {
        var target = room?.Trim();

        if (string.IsNullOrWhiteSpace(target) || ids == null || ids.Length == 0)
        {
            StatusKind = "warning";
            StatusMessage = "Δεν επιλέχθηκε καμία συσκευή.";
            return RedirectToPage();
        }

        var computers = await _context.Computers.Where(c => ids.Contains(c.Id)).ToListAsync();
        var by = UserPasswords.ActorName(User);
        var madeStatic = 0;

        foreach (var computer in computers)
        {
            if (computer.IsDynamic)
            {
                computer.IsDynamic = false;
                madeStatic++;
            }

            computer.Location = target;
            computer.UpdatedAt = DateTime.Now;
            computer.UpdatedBy = by;
        }

        await _context.SaveChangesAsync();

        StatusKind = "success";
        StatusMessage = $"➕ {computers.Count} συσκευές μπήκαν στον χώρο «{target}»." +
                        (madeStatic > 0 ? $" ({madeStatic} ήταν dynamic και έγιναν static.)" : string.Empty);

        return RedirectToPage();
    }

    /// <summary>Διαγραφή άδειου χώρου.</summary>
    public async Task<IActionResult> OnPostDeleteRoomAsync(string? location)
    {
        var name = location?.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            return RedirectToPage();
        }

        if ((await LoadLocationAsync(name)).Count > 0)
        {
            StatusKind = "warning";
            StatusMessage = "Ο χώρος έχει συσκευές — μετακίνησέ τες πρώτα.";
            return RedirectToPage();
        }

        var rooms = (await _context.Rooms.ToListAsync())
            .Where(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        _context.Rooms.RemoveRange(rooms);
        await _context.SaveChangesAsync();

        StatusKind = "success";
        StatusMessage = $"Ο χώρος «{name}» διαγράφηκε.";

        return RedirectToPage();
    }

    /// <summary>Μεταφορά επιλεγμένων συσκευών σε (νέο ή υπάρχοντα) χώρο.</summary>
    public async Task<IActionResult> OnPostAssignAsync(int[] ids, string? newLocation)
    {
        if (ids == null || ids.Length == 0)
        {
            StatusKind = "warning";
            StatusMessage = "Δεν επιλέχθηκε καμία συσκευή.";
            return RedirectToPage();
        }

        var target = string.IsNullOrWhiteSpace(newLocation) ? null : newLocation.Trim();
        var computers = await _context.Computers.Where(c => ids.Contains(c.Id)).ToListAsync();

        foreach (var computer in computers)
        {
            computer.Location = target;
            computer.UpdatedAt = DateTime.Now;
        }

        await _context.SaveChangesAsync();

        StatusKind = "success";
        StatusMessage = $"{computers.Count} συσκευές μεταφέρθηκαν στον χώρο «{DisplayName(target)}».";

        return RedirectToPage();
    }

    /// <summary>Μετονομασία χώρου (αλλάζει σε όλες τις συσκευές του).</summary>
    public async Task<IActionResult> OnPostRenameAsync(string? location, string? newName)
    {
        var target = string.IsNullOrWhiteSpace(newName) ? null : newName.Trim();
        var computers = await LoadLocationAsync(location);

        foreach (var computer in computers)
        {
            computer.Location = target;
            computer.UpdatedAt = DateTime.Now;
        }

        // Και η εγγραφή του χώρου (αν υπάρχει), μαζί με κάτοψη, τμήματα, θέσεις
        if (!string.IsNullOrWhiteSpace(location) && location != NoLocationKey && target != null)
        {
            var oldName = location.Trim();

            foreach (var room in (await _context.Rooms.ToListAsync()).Where(r => string.Equals(r.Name, oldName, StringComparison.OrdinalIgnoreCase)))
            {
                room.Name = target;
            }

            await _context.PlanItems.Where(p => p.Location == oldName).ExecuteUpdateAsync(u => u.SetProperty(p => p.Location, target));
            await _context.RoomPlans.Where(p => p.Location == oldName).ExecuteUpdateAsync(u => u.SetProperty(p => p.Location, target));
            await _context.SchoolClasses.Where(p => p.Location == oldName).ExecuteUpdateAsync(u => u.SetProperty(p => p.Location, target));
        }

        await _context.SaveChangesAsync();

        StatusKind = "success";
        StatusMessage = $"Ο χώρος «{DisplayName(location)}» έγινε «{DisplayName(target)}» ({computers.Count} συσκευές).";

        return RedirectToPage();
    }

    private async Task<List<Computer>> LoadLocationAsync(string? location)
    {
        var computers = await _context.Computers.ToListAsync();

        return computers.Where(c => !c.IsDynamic && MatchesLocation(c, location)).ToList();
    }

    public static bool MatchesLocation(Computer computer, string? location)
    {
        if (string.IsNullOrWhiteSpace(location) || location == NoLocationKey)
        {
            return string.IsNullOrWhiteSpace(computer.Location);
        }

        return string.Equals(computer.Location?.Trim(), location.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string DisplayName(string? location)
    {
        return string.IsNullOrWhiteSpace(location) || location == NoLocationKey ? NoLocationName : location;
    }

    public class LocationGroup
    {
        public string Name { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? AutoShutdown { get; set; }
        public int WithAgent { get; set; }
        public List<Computer> Computers { get; set; } = new();
        public bool IsEmpty => Computers.Count == 0;

        public int Total => Computers.Count(c => !c.IsEmptySlot);
        public int Online => Computers.Count(c => !c.IsEmptySlot && c.IsOnline == true);
        public int WithMac => Computers.Count(c => !c.IsEmptySlot && !string.IsNullOrWhiteSpace(c.MacAddress));

        public int OnlinePercent => Total == 0 ? 0 : (int)Math.Round(Online * 100.0 / Total);
        public int Offline => Computers.Count(c => !c.IsEmptySlot && c.IsOnline == false);
        public int Unnamed => Computers.Count(c => !c.IsEmptySlot && string.IsNullOrWhiteSpace(c.Name));

        /// <summary>Πολλές συσκευές: συμπαγής «χάρτης» με κουκκίδες αντί για ονόματα.</summary>
        public bool IsLarge => Computers.Count > 36;

        public List<(string Type, int Count)> TypeCounts => Computers
            .Where(c => !c.IsEmptySlot)
            .GroupBy(c => c.DisplayMachineType)
            .Select(g => (g.Key, g.Count()))
            .OrderByDescending(x => x.Item2)
            .ToList();
    }
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;
using SchoolNetworkManager.Services;
using LocationsPage = SchoolNetworkManager.Pages.Locations.IndexModel;

namespace SchoolNetworkManager.Pages.Locations;

/// <summary>Κάτοψη χώρου (ελεύθερη διάταξη) και πλάνο θέσεων ανά τμήμα.</summary>
public class PlanModel : PageModel
{
    private readonly AppDbContext _context;
    private readonly IWebHostEnvironment _environment;
    private readonly SettingsService _settings;

    public PlanModel(AppDbContext context, IWebHostEnvironment environment, SettingsService settings)
    {
        _context = context;
        _environment = environment;
        _settings = settings;
    }

    [BindProperty(SupportsGet = true)]
    public string? Location { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? ClassId { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool Print { get; set; }

    public string LocationName => Location ?? string.Empty;

    public AppSettings Settings => _settings.Current;

    public RoomPlan Room { get; set; } = new();
    public List<Computer> Devices { get; set; } = new();
    public List<PlanItem> Items { get; set; } = new();
    public List<SchoolClass> Classes { get; set; } = new();
    public SchoolClass? SelectedClass { get; set; }
    public Dictionary<int, string> SeatNames { get; set; } = new();
    public HashSet<int> OpenFaults { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (string.IsNullOrWhiteSpace(Location) || Location == LocationsPage.NoLocationKey)
        {
            return RedirectToPage("/Locations/Index");
        }

        await LoadAsync();

        if (Devices.Count == 0)
        {
            StatusMessage = "Ο χώρος δεν έχει συσκευές.";
            return RedirectToPage("/Locations/Index");
        }

        return Page();
    }

    private async Task LoadAsync()
    {
        var key = Location!.Trim();

        Devices = (await _context.Computers.AsNoTracking().ToListAsync())
            .Where(c => LocationsPage.MatchesLocation(c, key) && !c.IsEmptySlot)
            .OrderBy(c => NetworkTools.IpToNumber(c.IpAddress))
            .ToList();

        Room = await _context.RoomPlans.FirstOrDefaultAsync(r => r.Location == key)
               ?? new RoomPlan { Location = key };

        var ids = Devices.Select(d => d.Id).ToHashSet();

        Items = (await _context.PlanItems.Where(p => p.Location == key).ToListAsync())
            .Where(p => p.ComputerId == null || ids.Contains(p.ComputerId.Value))
            .ToList();

        Classes = await _context.SchoolClasses
            .Where(c => c.Location == key)
            .OrderBy(c => c.Name)
            .ToListAsync();

        SelectedClass = ClassId.HasValue ? Classes.FirstOrDefault(c => c.Id == ClassId.Value) : null;

        if (SelectedClass != null)
        {
            SeatNames = await _context.Seats
                .Where(s => s.SchoolClassId == SelectedClass.Id)
                .ToDictionaryAsync(s => s.ComputerId, s => s.StudentName);
        }

        OpenFaults = (await _context.Faults
                .Where(f => f.ResolvedAt == null)
                .Select(f => f.ComputerId)
                .ToListAsync())
            .ToHashSet();
    }

    // ---------------- Διάταξη ----------------

    public class LayoutItemDto
    {
        public int? ComputerId { get; set; }
        public string? Kind { get; set; }
        public string? Label { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double W { get; set; }
        public double H { get; set; }
    }

    public class LayoutDto
    {
        public double AspectRatio { get; set; }
        public List<LayoutItemDto> Items { get; set; } = new();
    }

    private static readonly string[] AllowedKinds = { "device", "desk", "door", "board", "teacher", "window", "label" };

    public async Task<IActionResult> OnPostLayoutAsync([FromBody] LayoutDto layout)
    {
        if (string.IsNullOrWhiteSpace(Location) || layout == null)
        {
            return BadRequest();
        }

        var key = Location.Trim();

        await _context.PlanItems.Where(p => p.Location == key).ExecuteDeleteAsync();

        foreach (var item in layout.Items.Take(500))
        {
            var kind = AllowedKinds.Contains(item.Kind) ? item.Kind! : "label";

            if (kind == "device" && item.ComputerId == null)
            {
                continue;
            }

            _context.PlanItems.Add(new PlanItem
            {
                Location = key,
                ComputerId = kind == "device" ? item.ComputerId : null,
                Kind = kind,
                Label = item.Label?.Trim() is { Length: > 0 } l ? (l.Length > 60 ? l[..60] : l) : null,
                X = Clamp(item.X),
                Y = Clamp(item.Y),
                W = Clamp(item.W),
                H = Clamp(item.H)
            });
        }

        var room = await _context.RoomPlans.FirstOrDefaultAsync(r => r.Location == key);

        if (room == null)
        {
            room = new RoomPlan { Location = key };
            _context.RoomPlans.Add(room);
        }

        room.AspectRatio = Math.Clamp(layout.AspectRatio <= 0 ? 1.6 : layout.AspectRatio, 0.4, 4);
        room.UpdatedAt = DateTime.Now;

        await _context.SaveChangesAsync();

        return new JsonResult(new { ok = true });
    }

    private static double Clamp(double value) => Math.Round(Math.Clamp(double.IsFinite(value) ? value : 0, 0, 100), 2);

    // ---------------- Εικόνα φόντου ----------------

    public async Task<IActionResult> OnPostBackgroundAsync(IFormFile? file, bool remove)
    {
        if (string.IsNullOrWhiteSpace(Location))
        {
            return RedirectToPage("/Locations/Index");
        }

        var key = Location.Trim();
        var room = await _context.RoomPlans.FirstOrDefaultAsync(r => r.Location == key);

        if (room == null)
        {
            room = new RoomPlan { Location = key };
            _context.RoomPlans.Add(room);
        }

        if (remove)
        {
            room.BackgroundPath = null;
            StatusMessage = "Η εικόνα φόντου αφαιρέθηκε.";
        }
        else if (file != null && file.Length > 0)
        {
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

            if (!new[] { ".png", ".jpg", ".jpeg", ".webp" }.Contains(extension) || file.Length > 8 * 1024 * 1024)
            {
                StatusMessage = "⚠️ Η εικόνα πρέπει να είναι PNG / JPG / WEBP έως 8MB.";
                return RedirectToPage(new { Location, ClassId });
            }

            var folder = Path.Combine(_environment.WebRootPath, "uploads", "plans");
            Directory.CreateDirectory(folder);

            var fileName = $"plan_{Guid.NewGuid():N}{extension}";

            await using (var stream = new FileStream(Path.Combine(folder, fileName), FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            room.BackgroundPath = $"/uploads/plans/{fileName}";
            StatusMessage = "🖼️ Η εικόνα φόντου ανέβηκε. Η κάτοψη πήρε τις αναλογίες της — τακτοποίησε τις συσκευές και πάτα Αποθήκευση.";
        }

        room.UpdatedAt = DateTime.Now;
        await _context.SaveChangesAsync();

        return RedirectToPage(new { Location, ClassId, edit = true });
    }

    // ---------------- Τμήματα & θέσεις ----------------

    public async Task<IActionResult> OnPostAddClassAsync(string? name, string? teacher, string? schedule)
    {
        if (string.IsNullOrWhiteSpace(Location) || string.IsNullOrWhiteSpace(name))
        {
            StatusMessage = "⚠️ Γράψε όνομα τμήματος.";
            return RedirectToPage(new { Location });
        }

        var schoolClass = new SchoolClass
        {
            Location = Location.Trim(),
            Name = name.Trim(),
            Teacher = string.IsNullOrWhiteSpace(teacher) ? null : teacher.Trim(),
            Schedule = string.IsNullOrWhiteSpace(schedule) ? null : schedule.Trim(),
            CreatedAt = DateTime.Now
        };

        _context.SchoolClasses.Add(schoolClass);
        await _context.SaveChangesAsync();

        StatusMessage = $"Το τμήμα «{schoolClass.Name}» προστέθηκε. Πάτα πάνω σε κάθε υπολογιστή για να γράψεις ποιος κάθεται.";

        return RedirectToPage(new { Location, ClassId = schoolClass.Id, seats = true });
    }

    public async Task<IActionResult> OnPostDeleteClassAsync(int id)
    {
        var schoolClass = await _context.SchoolClasses.FindAsync(id);

        if (schoolClass != null)
        {
            _context.SchoolClasses.Remove(schoolClass);
            await _context.SaveChangesAsync();
            await _context.Seats.Where(s => s.SchoolClassId == id).ExecuteDeleteAsync();
            StatusMessage = $"Το τμήμα «{schoolClass.Name}» διαγράφηκε.";
        }

        return RedirectToPage(new { Location });
    }

    public class SeatDto
    {
        public int ComputerId { get; set; }
        public string? Name { get; set; }
    }

    public class SeatsDto
    {
        public int ClassId { get; set; }
        public List<SeatDto> Seats { get; set; } = new();
    }

    public async Task<IActionResult> OnPostSeatsAsync([FromBody] SeatsDto data)
    {
        if (data == null || !await _context.SchoolClasses.AnyAsync(c => c.Id == data.ClassId))
        {
            return BadRequest();
        }

        await _context.Seats.Where(s => s.SchoolClassId == data.ClassId).ExecuteDeleteAsync();

        foreach (var seat in data.Seats.Take(500))
        {
            var name = seat.Name?.Trim();

            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            _context.Seats.Add(new Seat
            {
                SchoolClassId = data.ClassId,
                ComputerId = seat.ComputerId,
                StudentName = name.Length > 80 ? name[..80] : name
            });
        }

        await _context.SaveChangesAsync();

        return new JsonResult(new { ok = true });
    }
}

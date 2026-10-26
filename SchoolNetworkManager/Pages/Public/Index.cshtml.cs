using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;
using SchoolNetworkManager.Services;

namespace SchoolNetworkManager.Pages.Public;

/// <summary>
/// Δημόσια σελίδα (χωρίς κωδικό): δήλωση βλάβης από μαθητές / εκπαιδευτικούς και κανονισμοί.
/// Δεν δείχνει IP, MAC ή κωδικούς. Οι δηλώσεις περιμένουν έγκριση από την ομάδα.
/// </summary>
public class IndexModel : PageModel
{
    private readonly AppDbContext _context;
    private readonly SettingsService _settings;
    private readonly PublicRateLimiter _limiter;
    private readonly BackupService _storage;

    public IndexModel(AppDbContext context, SettingsService settings, PublicRateLimiter limiter, BackupService storage)
    {
        _context = context;
        _settings = settings;
        _limiter = limiter;
        _storage = storage;
    }

    /// <summary>Αρχεία της βιβλιοθήκης που είναι σημειωμένα «δημόσια».</summary>
    public List<ToolFile> PublicFiles { get; set; } = new();

    public async Task<IActionResult> OnGetFileAsync(int id)
    {
        if (!_settings.Current.PublicPortal && User.Identity?.IsAuthenticated != true)
        {
            return NotFound();
        }

        return await SchoolNetworkManager.Pages.Library.IndexModel.Download(_context, _storage, id, publicOnly: true, this);
    }

    public AppSettings Settings => _settings.Current;

    [BindProperty(SupportsGet = true)]
    public string? Location { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? Device { get; set; }

    [BindProperty] public string? DeviceText { get; set; }
    [BindProperty] public string? Description { get; set; }
    [BindProperty] public string? Severity { get; set; }
    [BindProperty] public string? ReporterName { get; set; }
    [BindProperty] public string? ReporterRole { get; set; }
    [BindProperty] public string? ClassName { get; set; }

    /// <summary>Παγίδα για bots — οι άνθρωποι δεν τη βλέπουν.</summary>
    [BindProperty] public string? Website { get; set; }

    public List<LocationOption> Locations { get; set; } = new();
    public List<SchoolDocument> Documents { get; set; } = new();
    public string? Error { get; set; }

    [TempData] public bool Sent { get; set; }
    [TempData] public string? SentWhat { get; set; }

    public bool Enabled { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadAsync();
        return Page();
    }

    private async Task LoadAsync()
    {
        var signedIn = User.Identity?.IsAuthenticated == true;
        Enabled = _settings.Current.PublicPortal || signedIn;

        if (!Enabled)
        {
            return;
        }

        var devices = (await _context.Computers.AsNoTracking().ToListAsync())
            .Where(c => !c.IsEmptySlot && !string.IsNullOrWhiteSpace(c.Location))
            .OrderBy(c => NetworkTools.IpToNumber(c.IpAddress))
            .ToList();

        Locations = devices
            .GroupBy(c => c.Location!.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key)
            .Select(g => new LocationOption
            {
                Name = g.Key,
                Devices = g.Select(c => new DeviceOption { Id = c.Id, Label = Label(c), Icon = DeviceSpecs.TypeIcon(c.MachineType) })
                    .OrderBy(d => d.Label, StringComparer.CurrentCultureIgnoreCase)
                    .ToList()
            })
            .ToList();

        Documents = (await SchoolDocuments.EnsureAsync(_context)).Where(d => d.IsPublic).ToList();
        PublicFiles = await _context.ToolFiles.AsNoTracking().Where(t => t.IsPublic).OrderBy(t => t.Title).ToListAsync();
    }

    /// <summary>Ό,τι γράφει η ετικέτα της συσκευής — όχι IP.</summary>
    private static string Label(Computer c)
    {
        if (!string.IsNullOrWhiteSpace(c.Name))
        {
            return c.Name.Trim();
        }

        var last = c.IpAddress.Split('.').LastOrDefault();
        return $"{c.DisplayMachineType} #{last}";
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadAsync();

        if (!Enabled)
        {
            return Page();
        }

        // Bot: κάνουμε πως πέτυχε, χωρίς να γράψουμε τίποτα.
        if (!string.IsNullOrEmpty(Website))
        {
            Sent = true;
            return RedirectToPage();
        }

        var location = Locations.FirstOrDefault(l => string.Equals(l.Name, Location?.Trim(), StringComparison.OrdinalIgnoreCase));
        var device = location?.Devices.FirstOrDefault(d => d.Id == Device);
        var description = Description?.Trim() ?? string.Empty;
        var deviceText = DeviceText?.Trim();

        if (location == null)
        {
            Error = "Διάλεξε εργαστήριο / χώρο.";
            return Page();
        }

        if (device == null && string.IsNullOrWhiteSpace(deviceText))
        {
            Error = "Διάλεξε τη συσκευή ή γράψε ποια είναι (π.χ. «προβολέας», «2η θέση δίπλα στο παράθυρο»).";
            return Page();
        }

        if (description.Length < 5)
        {
            Error = "Γράψε λίγα λόγια για το πρόβλημα.";
            return Page();
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        if (!_limiter.TryAcquire(ip))
        {
            Error = "Έγιναν πολλές δηλώσεις από αυτή τη συσκευή. Δοκίμασε ξανά σε λίγο ή ενημέρωσε τον καθηγητή σου.";
            return Page();
        }

        _context.FaultReports.Add(new FaultReport
        {
            Location = location.Name,
            ComputerId = device?.Id,
            DeviceText = Cut(deviceText, 120),
            Description = Cut(description, 1000)!,
            Severity = Severity == Fault.SeverityDown ? Fault.SeverityDown : Fault.SeverityMinor,
            ReporterName = Cut(ReporterName?.Trim(), 80),
            ReporterRole = FaultReport.Roles.Contains(ReporterRole) ? ReporterRole : null,
            ClassName = Cut(ClassName?.Trim(), 40),
            CreatedAt = DateTime.Now,
            IpAddress = ip,
            Status = FaultReport.StatusNew
        });

        await _context.SaveChangesAsync();

        Sent = true;
        SentWhat = $"{location.Name} · {device?.Label ?? deviceText}";

        return RedirectToPage(new { Location = location.Name });
    }

    private static string? Cut(string? text, int max) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Length > max ? text[..max] : text;

    public class LocationOption
    {
        public string Name { get; set; } = string.Empty;
        public List<DeviceOption> Devices { get; set; } = new();
    }

    public class DeviceOption
    {
        public int Id { get; set; }
        public string Label { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
    }
}

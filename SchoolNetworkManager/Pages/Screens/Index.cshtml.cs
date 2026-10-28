using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;
using SchoolNetworkManager.Services;

namespace SchoolNetworkManager.Pages.Screens;

/// <summary>Τοίχος οθονών: ζωντανές μικρογραφίες των υπολογιστών που έχουν agent.</summary>
public class IndexModel : PageModel
{
    private readonly AppDbContext _context;
    private readonly SettingsService _settings;
    private readonly ScreenStore _screens;

    public IndexModel(AppDbContext context, SettingsService settings, ScreenStore screens)
    {
        _context = context;
        _settings = settings;
        _screens = screens;
    }

    public List<Tile> Tiles { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? Location { get; set; }

    public List<string> Locations { get; set; } = new();

    public bool RustDeskReady { get; set; }

    public record Tile(int Id, string Name, string? Location, bool Online, string? RustDeskId);

    public async Task OnGetAsync()
    {
        RustDeskReady = !string.IsNullOrWhiteSpace(_settings.Current.RustDeskHost);

        var agentIds = await _context.AgentReports.Select(r => r.ComputerId).ToListAsync();
        var reports = await _context.AgentReports.AsNoTracking().ToDictionaryAsync(r => r.ComputerId, r => r.RustDeskId);

        var computers = await _context.Computers.AsNoTracking()
            .Where(c => !c.IsDynamic && agentIds.Contains(c.Id))
            .ToListAsync();

        Locations = computers
            .Select(c => string.IsNullOrWhiteSpace(c.Location) ? "Χωρίς χώρο" : c.Location!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        if (!string.IsNullOrWhiteSpace(Location))
        {
            computers = computers
                .Where(c => string.Equals(string.IsNullOrWhiteSpace(c.Location) ? "Χωρίς χώρο" : c.Location!.Trim(),
                                          Location.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        Tiles = computers
            .OrderBy(c => NetworkTools.IpToNumber(c.IpAddress))
            .Select(c => new Tile(c.Id,
                string.IsNullOrWhiteSpace(c.Name) ? c.IpAddress : c.Name!,
                c.Location,
                c.IsOnline == true,
                reports.GetValueOrDefault(c.Id)))
            .ToList();
    }

    /// <summary>Το σήμα «κάποιος βλέπει» — η σελίδα το καλεί τακτικά ώστε οι agents να τραβούν οθόνη.</summary>
    public IActionResult OnGetPing()
    {
        _screens.MarkWatched();

        var fresh = _context.AgentReports
            .Select(r => r.ComputerId)
            .ToList()
            .Where(id => _screens.Has(id))
            .ToList();

        return new JsonResult(new { ok = true, ready = fresh });
    }

    /// <summary>Σερβίρει την τελευταία μικρογραφία ενός υπολογιστή (μόνο σε συνδεδεμένο χρήστη).</summary>
    public IActionResult OnGetImage(int id)
    {
        var shot = _screens.Get(id);

        if (shot == null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "no-store";
        return File(shot.Data, shot.ContentType);
    }
}

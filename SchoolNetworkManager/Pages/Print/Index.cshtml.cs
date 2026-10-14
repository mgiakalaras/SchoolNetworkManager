using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;
using SchoolNetworkManager.Services;
using LocationsPage = SchoolNetworkManager.Pages.Locations.IndexModel;

namespace SchoolNetworkManager.Pages.Print;

public class IndexModel : PageModel
{
    private readonly AppDbContext _context;
    private readonly SettingsService _settings;

    public IndexModel(AppDbContext context, SettingsService settings)
    {
        _context = context;
        _settings = settings;
    }

    /// <summary>Χώρος για εκτύπωση. Κενό = όλοι οι χώροι.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Location { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool ShowMac { get; set; } = true;

    [BindProperty(SupportsGet = true)]
    public bool ShowType { get; set; } = true;

    [BindProperty(SupportsGet = true)]
    public bool ShowStatus { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool ShowNotes { get; set; } = true;

    [BindProperty(SupportsGet = true)]
    public bool ShowCheck { get; set; } = true;

    [BindProperty(SupportsGet = true)]
    public bool IncludeEmpty { get; set; } = true;

    public AppSettings Settings => _settings.Current;

    public List<string> AllLocationKeys { get; set; } = new();

    public List<PrintGroup> Groups { get; set; } = new();

    public async Task OnGetAsync()
    {
        var computers = await _context.Computers.AsNoTracking().ToListAsync();

        AllLocationKeys = computers
            .Select(c => string.IsNullOrWhiteSpace(c.Location) ? LocationsPage.NoLocationKey : c.Location.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k == LocationsPage.NoLocationKey)
            .ThenBy(k => k)
            .ToList();

        var selected = string.IsNullOrWhiteSpace(Location)
            ? AllLocationKeys
            : new List<string> { Location };

        foreach (var key in selected)
        {
            var items = computers
                .Where(c => LocationsPage.MatchesLocation(c, key))
                .Where(c => IncludeEmpty || !c.IsEmptySlot)
                .OrderBy(c => NetworkTools.IpToNumber(c.IpAddress))
                .ToList();

            if (items.Count == 0)
            {
                continue;
            }

            Groups.Add(new PrintGroup
            {
                Name = key == LocationsPage.NoLocationKey ? LocationsPage.NoLocationName : key,
                Computers = items
            });
        }
    }

    public static string LocationLabel(string key) =>
        key == LocationsPage.NoLocationKey ? LocationsPage.NoLocationName : key;

    public class PrintGroup
    {
        public string Name { get; set; } = string.Empty;
        public List<Computer> Computers { get; set; } = new();
    }
}

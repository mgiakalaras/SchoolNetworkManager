using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;
using SchoolNetworkManager.Services;

namespace SchoolNetworkManager.Pages.Faults;

/// <summary>
/// Απλή σελίδα «Τι δεν λειτουργεί» για τους συναδέλφους / για εκτύπωση.
/// Χωρίς κωδικό μόνο αν το επιτρέπουν οι Ρυθμίσεις. Δεν δείχνει IP, MAC ή κωδικούς.
/// </summary>
public class PublicModel : PageModel
{
    private readonly AppDbContext _context;
    private readonly SettingsService _settings;

    public PublicModel(AppDbContext context, SettingsService settings)
    {
        _context = context;
        _settings = settings;
    }

    [BindProperty(SupportsGet = true)]
    public bool Print { get; set; }

    public AppSettings Settings => _settings.Current;

    public List<FaultRow> Open { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var signedIn = User.Identity?.IsAuthenticated == true ||
                       string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ADMIN_PASSWORD"));

        if (!signedIn && !_settings.Current.PublicFaultsPage)
        {
            return RedirectToPage("/Login", new { ReturnUrl = "/Faults/Public" });
        }

        Open = (await FaultQueries.LoadAsync(_context, includeClosedDays: 0))
            .Where(r => r.Fault.IsOpen)
            .ToList();

        return Page();
    }
}

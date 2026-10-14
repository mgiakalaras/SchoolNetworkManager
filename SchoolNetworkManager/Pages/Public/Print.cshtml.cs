using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;
using SchoolNetworkManager.Services;

namespace SchoolNetworkManager.Pages.Public;

/// <summary>Καλαίσθητη εκτύπωση κανονισμού: «Επίσημο» έγγραφο ή «Αφίσα» για τον τοίχο του εργαστηρίου.</summary>
public class PrintModel : PageModel
{
    private readonly AppDbContext _context;
    private readonly SettingsService _settings;

    public PrintModel(AppDbContext context, SettingsService settings)
    {
        _context = context;
        _settings = settings;
    }

    [BindProperty(SupportsGet = true)]
    public string? Doc { get; set; }

    /// <summary>official | poster</summary>
    [BindProperty(SupportsGet = true)]
    public string? Style { get; set; }

    public AppSettings Settings => _settings.Current;
    public SchoolDocument Current { get; set; } = new();
    public List<DocText.DocSection> Sections { get; set; } = new();
    public bool IsPoster => Style == "poster";
    public bool SignedIn => User.Identity?.IsAuthenticated == true;

    public async Task<IActionResult> OnGetAsync()
    {
        if (!_settings.Current.PublicPortal && !SignedIn)
        {
            return RedirectToPage("/Login");
        }

        var documents = await SchoolDocuments.EnsureAsync(_context);
        var document = documents.FirstOrDefault(d => d.Key == Doc) ?? documents[0];

        if (!document.IsPublic && !SignedIn)
        {
            return RedirectToPage("/Public/Index");
        }

        Current = document;
        Doc = document.Key;
        Style ??= document.Key == SchoolDocument.StudentRules ? "poster" : "official";
        Sections = DocText.Parse(document.Body);

        return Page();
    }
}

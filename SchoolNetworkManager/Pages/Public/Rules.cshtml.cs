using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;
using SchoolNetworkManager.Services;
using RulesPage = SchoolNetworkManager.Pages.Rules.IndexModel;

namespace SchoolNetworkManager.Pages.Public;

/// <summary>Δημόσια προβολή κανονισμού / κανόνων (μόνο όσα έχουν σημειωθεί δημόσια).</summary>
public class RulesModel : PageModel
{
    private readonly AppDbContext _context;
    private readonly SettingsService _settings;
    private readonly IWebHostEnvironment _environment;

    public RulesModel(AppDbContext context, SettingsService settings, IWebHostEnvironment environment)
    {
        _context = context;
        _settings = settings;
        _environment = environment;
    }

    [BindProperty(SupportsGet = true)]
    public string? Doc { get; set; }

    public AppSettings Settings => _settings.Current;
    public SchoolDocument? Current { get; set; }
    public List<SchoolDocument> Documents { get; set; } = new();

    private async Task<bool> LoadAsync()
    {
        var signedIn = User.Identity?.IsAuthenticated == true;

        if (!_settings.Current.PublicPortal && !signedIn)
        {
            return false;
        }

        Documents = (await SchoolDocuments.EnsureAsync(_context)).Where(d => d.IsPublic || signedIn).ToList();
        Current = Documents.FirstOrDefault(d => d.Key == Doc) ?? Documents.FirstOrDefault();

        return Current != null;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadAsync())
        {
            return RedirectToPage("/Public/Index");
        }

        return Page();
    }

    public async Task<IActionResult> OnGetWordAsync()
    {
        if (!await LoadAsync())
        {
            return NotFound();
        }

        var bytes = DocText.ToDocx(Current!.Title, _settings.Current.SchoolTitle, Current.Body);

        return File(bytes, "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            $"{SchoolDocuments.ShortName(Current.Key)}.docx");
    }

    public async Task<IActionResult> OnGetFileAsync()
    {
        if (!await LoadAsync() || string.IsNullOrWhiteSpace(Current!.FilePath) || !Current.FilePath.StartsWith("/uploads/docs/"))
        {
            return NotFound();
        }

        var path = Path.Combine(_environment.WebRootPath, "uploads", "docs", Path.GetFileName(Current.FilePath));

        if (!System.IO.File.Exists(path))
        {
            return NotFound();
        }

        return PhysicalFile(path, RulesPage.ContentType(path), Current.FileName ?? Path.GetFileName(path));
    }
}

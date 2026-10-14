using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;
using SchoolNetworkManager.Services;

namespace SchoolNetworkManager.Pages.Rules;

/// <summary>Κανονισμός εργαστηρίων και κανόνες μαθητών: προβολή, επεξεργασία, Word, ανέβασμα.</summary>
public class IndexModel : PageModel
{
    private static readonly string[] AllowedFiles = { ".docx", ".pdf", ".odt", ".doc" };

    private readonly AppDbContext _context;
    private readonly IWebHostEnvironment _environment;
    private readonly SettingsService _settings;

    public IndexModel(AppDbContext context, IWebHostEnvironment environment, SettingsService settings)
    {
        _context = context;
        _environment = environment;
        _settings = settings;
    }

    [BindProperty(SupportsGet = true)]
    public string? Doc { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool Edit { get; set; }

    public List<SchoolDocument> Documents { get; set; } = new();
    public SchoolDocument Current { get; set; } = new();
    public bool PortalEnabled => _settings.Current.PublicPortal;
    public string SchoolTitle => _settings.Current.SchoolTitle;

    [TempData] public string? StatusMessage { get; set; }
    [TempData] public string? StatusKind { get; set; }

    private async Task<SchoolDocument> LoadAsync()
    {
        Documents = await SchoolDocuments.EnsureAsync(_context);
        Current = Documents.FirstOrDefault(d => d.Key == Doc) ?? Documents[0];
        Doc = Current.Key;
        return Current;
    }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostSaveAsync(string? title, string? body, bool isPublic)
    {
        var document = await LoadAsync();

        document.Title = string.IsNullOrWhiteSpace(title) ? DocText.Default(document.Key).Title : title.Trim();
        document.Body = (body ?? string.Empty).Replace("\r\n", "\n").Trim();
        document.IsPublic = isPublic;
        document.UpdatedAt = DateTime.Now;
        document.UpdatedBy = UserPasswords.ActorName(User);
        await _context.SaveChangesAsync();

        Ok("✓ Αποθηκεύτηκε.");
        return RedirectToPage(new { Doc });
    }

    public async Task<IActionResult> OnPostResetAsync()
    {
        var document = await LoadAsync();
        var (title, body) = DocText.Default(document.Key);

        document.Title = title;
        document.Body = body;
        document.UpdatedAt = DateTime.Now;
        document.UpdatedBy = UserPasswords.ActorName(User);
        await _context.SaveChangesAsync();

        Ok("↺ Μπήκε ξανά το αρχικό κείμενο.");
        return RedirectToPage(new { Doc, Edit = true });
    }

    /// <summary>Λήψη του κειμένου ως Word για επεξεργασία.</summary>
    public async Task<IActionResult> OnGetWordAsync()
    {
        var document = await LoadAsync();
        var subtitle = $"{_settings.Current.SchoolTitle} · {document.UpdatedAt:dd/MM/yyyy}";
        var bytes = DocText.ToDocx(document.Title, subtitle, document.Body);

        return File(bytes, "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            $"{SchoolDocuments.ShortName(document.Key)}.docx");
    }

    /// <summary>Λήψη του αρχείου που ανέβηκε (όπως ανέβηκε).</summary>
    public async Task<IActionResult> OnGetFileAsync()
    {
        var document = await LoadAsync();
        var path = PhysicalPath(document.FilePath);

        if (path == null || !System.IO.File.Exists(path))
        {
            return NotFound();
        }

        return PhysicalFile(path, ContentType(path), document.FileName ?? Path.GetFileName(path));
    }

    public async Task<IActionResult> OnPostUploadAsync(IFormFile? file, bool importText)
    {
        var document = await LoadAsync();

        if (file == null || file.Length == 0)
        {
            Fail("Διάλεξε αρχείο.");
            return RedirectToPage(new { Doc });
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

        if (!AllowedFiles.Contains(extension) || file.Length > 15 * 1024 * 1024)
        {
            Fail("Επιτρέπονται Word (.docx/.doc), PDF ή .odt έως 15MB.");
            return RedirectToPage(new { Doc });
        }

        var folder = Path.Combine(_environment.WebRootPath, "uploads", "docs");
        Directory.CreateDirectory(folder);

        var fileName = $"{document.Key}_{Guid.NewGuid():N}{extension}";
        var fullPath = Path.Combine(folder, fileName);

        await using (var stream = new FileStream(fullPath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        var message = "📎 Το αρχείο ανέβηκε.";

        if (extension == ".docx" && importText)
        {
            try
            {
                await using var read = System.IO.File.OpenRead(fullPath);
                var imported = DocText.ImportDocx(read);

                if (!string.IsNullOrWhiteSpace(imported.Body))
                {
                    document.Body = imported.Body;

                    if (!string.IsNullOrWhiteSpace(imported.Title))
                    {
                        document.Title = imported.Title.Length > 160 ? imported.Title[..160] : imported.Title;
                    }

                    message += " Το κείμενο της σελίδας ενημερώθηκε από το Word.";
                }
            }
            catch (Exception)
            {
                message += " ⚠️ Δεν διαβάστηκε το κείμενο του Word — έμεινε το προηγούμενο.";
            }
        }

        DeleteFile(document.FilePath);
        document.FilePath = $"/uploads/docs/{fileName}";
        document.FileName = Path.GetFileName(file.FileName);
        document.UpdatedAt = DateTime.Now;
        document.UpdatedBy = UserPasswords.ActorName(User);
        await _context.SaveChangesAsync();

        Ok(message);
        return RedirectToPage(new { Doc });
    }

    public async Task<IActionResult> OnPostRemoveFileAsync()
    {
        var document = await LoadAsync();

        DeleteFile(document.FilePath);
        document.FilePath = null;
        document.FileName = null;
        document.UpdatedAt = DateTime.Now;
        document.UpdatedBy = UserPasswords.ActorName(User);
        await _context.SaveChangesAsync();

        Ok("Το αρχείο αφαιρέθηκε. Μένει το κείμενο της σελίδας.");
        return RedirectToPage(new { Doc });
    }

    private string? PhysicalPath(string? webPath)
    {
        if (string.IsNullOrWhiteSpace(webPath) || !webPath.StartsWith("/uploads/docs/"))
        {
            return null;
        }

        return Path.Combine(_environment.WebRootPath, "uploads", "docs", Path.GetFileName(webPath));
    }

    private void DeleteFile(string? webPath)
    {
        var path = PhysicalPath(webPath);

        try
        {
            if (path != null && System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }

    public static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".doc" => "application/msword",
        ".odt" => "application/vnd.oasis.opendocument.text",
        _ => "application/octet-stream"
    };

    private void Ok(string message) { StatusKind = "success"; StatusMessage = message; }
    private void Fail(string message) { StatusKind = "danger"; StatusMessage = "⚠️ " + message; }
}

using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;
using SchoolNetworkManager.Services;

namespace SchoolNetworkManager.Pages.Library;

/// <summary>Βιβλιοθήκη εργαλείων: utilities, εγκαταστάτες, scripts, drivers και σύνδεσμοι για την ομάδα.</summary>
[RequestSizeLimit(MaxUpload)]
[RequestFormLimits(MultipartBodyLengthLimit = MaxUpload)]
public class IndexModel : PageModel
{
    public const long MaxUpload = 8L * 1024 * 1024 * 1024;   // 8 GB (π.χ. ISO)

    private static readonly string[] Blocked = { ".aspx", ".cshtml", ".config" };

    private readonly AppDbContext _context;
    private readonly BackupService _storage;

    public IndexModel(AppDbContext context, BackupService storage)
    {
        _context = context;
        _storage = storage;
    }

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string? C { get; set; }

    public List<ToolFile> Items { get; set; } = new();
    public long UsedBytes { get; set; }
    public long? FreeBytes { get; set; }
    public int Total { get; set; }

    [TempData] public string? StatusMessage { get; set; }
    [TempData] public string? StatusKind { get; set; }

    public async Task OnGetAsync()
    {
        var all = await _context.ToolFiles.AsNoTracking().ToListAsync();
        Total = all.Count;
        UsedBytes = all.Sum(t => t.Size);

        try
        {
            FreeBytes = new DriveInfo(_storage.LibraryDirectory).AvailableFreeSpace;
        }
        catch (Exception)
        {
            FreeBytes = null;
        }

        var term = Q?.Trim();

        Items = all
            .Where(t => string.IsNullOrEmpty(C) || t.Category == C)
            .Where(t => string.IsNullOrEmpty(term) ||
                        t.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                        (t.Description ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase) ||
                        (t.FileName ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => Array.IndexOf(ToolFile.Categories, t.Category))
            .ThenBy(t => t.Title)
            .ToList();
    }

    public async Task<IActionResult> OnPostSaveAsync(int id, string? title, string? description, string? category,
        string? version, string? url, bool isPublic, IFormFile? file)
    {
        var item = id > 0 ? await _context.ToolFiles.FindAsync(id) : new ToolFile();

        if (item == null)
        {
            return RedirectToPage();
        }

        if (id > 0 && !CanChange(item))
        {
            return Fail("Μόνο όποιος το ανέβασε ή διαχειριστής μπορεί να το αλλάξει.");
        }

        var link = url?.Trim();

        if (!string.IsNullOrEmpty(link) && !(Uri.TryCreate(link, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)))
        {
            return Fail("Ο σύνδεσμος πρέπει να ξεκινά με http:// ή https://");
        }

        if (id == 0 && (file == null || file.Length == 0) && string.IsNullOrEmpty(link))
        {
            return Fail("Διάλεξε αρχείο ή γράψε σύνδεσμο.");
        }

        if (file is { Length: > 0 })
        {
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

            if (Blocked.Contains(extension))
            {
                return Fail("Αυτός ο τύπος αρχείου δεν επιτρέπεται.");
            }

            var stored = $"{Guid.NewGuid():N}{extension}";
            var path = Path.Combine(_storage.LibraryDirectory, stored);

            await using (var input = file.OpenReadStream())
            await using (var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true))
            using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[1 << 20];
                int read;

                while ((read = await input.ReadAsync(buffer)) > 0)
                {
                    sha.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read));
                }

                item.Sha256 = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
            }

            DeleteStored(item.StoredName);
            item.StoredName = stored;
            item.FileName = Path.GetFileName(file.FileName);
            item.Size = file.Length;
        }

        item.Title = string.IsNullOrWhiteSpace(title)
            ? Path.GetFileNameWithoutExtension(item.FileName ?? link ?? "Χωρίς τίτλο")
            : Cut(title, 120)!;
        item.Description = Cut(description, 1000);
        item.Category = ToolFile.Categories.Contains(category) ? category! : "📦 Άλλο";
        item.Version = Cut(version, 40);
        item.Url = string.IsNullOrEmpty(link) ? null : Cut(link, 500);
        item.IsPublic = isPublic;

        if (id == 0)
        {
            item.UploadedAt = DateTime.Now;
            item.UploadedBy = UserPasswords.ActorName(User);
            _context.ToolFiles.Add(item);
        }
        else
        {
            item.UpdatedAt = DateTime.Now;
        }

        await _context.SaveChangesAsync();

        StatusKind = "success";
        StatusMessage = id == 0 ? $"📦 Προστέθηκε «{item.Title}»." : $"✓ Αποθηκεύτηκε «{item.Title}».";

        return RedirectToPage(new { C });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var item = await _context.ToolFiles.FindAsync(id);

        if (item == null)
        {
            return RedirectToPage();
        }

        if (!CanChange(item))
        {
            return Fail("Μόνο όποιος το ανέβασε ή διαχειριστής μπορεί να το σβήσει.");
        }

        DeleteStored(item.StoredName);
        _context.ToolFiles.Remove(item);
        await _context.SaveChangesAsync();

        StatusKind = "success";
        StatusMessage = $"🗑️ Σβήστηκε «{item.Title}».";
        return RedirectToPage(new { C });
    }

    public async Task<IActionResult> OnGetDownloadAsync(int id) => await Download(_context, _storage, id, publicOnly: false, this);

    /// <summary>Κοινό με τη δημόσια σελίδα.</summary>
    public static async Task<IActionResult> Download(AppDbContext db, BackupService storage, int id, bool publicOnly, PageModel page)
    {
        var item = await db.ToolFiles.FindAsync(id);

        if (item == null || (publicOnly && !item.IsPublic))
        {
            return page.NotFound();
        }

        item.Downloads++;
        await db.SaveChangesAsync();

        if (item.IsLink)
        {
            return page.Redirect(item.Url!);
        }

        var path = string.IsNullOrEmpty(item.StoredName) ? null : Path.Combine(storage.LibraryDirectory, Path.GetFileName(item.StoredName));

        if (path == null || !System.IO.File.Exists(path))
        {
            return page.NotFound();
        }

        return page.PhysicalFile(path, "application/octet-stream", item.FileName ?? Path.GetFileName(path), enableRangeProcessing: true);
    }

    public bool CanChange(ToolFile item) =>
        User.Identity?.IsAuthenticated != true ||
        UserPasswords.IsAdmin(User) ||
        string.Equals(item.UploadedBy, UserPasswords.ActorName(User), StringComparison.OrdinalIgnoreCase);

    private void DeleteStored(string? stored)
    {
        if (string.IsNullOrEmpty(stored))
        {
            return;
        }

        try
        {
            var path = Path.Combine(_storage.LibraryDirectory, Path.GetFileName(stored));
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
        catch (IOException)
        {
        }
    }

    private IActionResult Fail(string message)
    {
        StatusKind = "danger";
        StatusMessage = "⚠️ " + message;
        return RedirectToPage(new { C });
    }

    private static string? Cut(string? value, int max)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) ? null : text.Length > max ? text[..max] : text;
    }
}

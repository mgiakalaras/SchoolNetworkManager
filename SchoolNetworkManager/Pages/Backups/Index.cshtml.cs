using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SchoolNetworkManager.Models;
using SchoolNetworkManager.Services;

namespace SchoolNetworkManager.Pages.Backups;

/// <summary>Αντίγραφα ασφαλείας: λίστα, δημιουργία, λήψη, επαναφορά (μόνο διαχειριστές).</summary>
[RequestSizeLimit(300 * 1024 * 1024)]
[RequestFormLimits(MultipartBodyLengthLimit = 300 * 1024 * 1024)]
public class IndexModel : PageModel
{
    private readonly BackupService _backups;
    private readonly SettingsService _settings;

    public IndexModel(BackupService backups, SettingsService settings)
    {
        _backups = backups;
        _settings = settings;
    }

    public List<BackupService.BackupInfo> Items { get; set; } = new();
    public AppSettings Settings => _settings.Current;
    public long DatabaseSize { get; set; }
    public long TotalSize { get; set; }
    public string Folder { get; set; } = string.Empty;

    [TempData] public string? StatusMessage { get; set; }
    [TempData] public string? StatusKind { get; set; }

    public void OnGet()
    {
        Items = _backups.List();
        DatabaseSize = _backups.DatabaseSize;
        TotalSize = Items.Sum(i => i.Size);
        Folder = _backups.BackupDirectory;
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        var created = await _backups.CreateAsync(BackupService.KindManual);
        Ok($"💾 Φτιάχτηκε αντίγραφο ({BackupService.FormatSize(created.Size)}).");
        return RedirectToPage();
    }

    public IActionResult OnGetDownload(string name)
    {
        var path = _backups.PathOf(name);
        return path == null ? NotFound() : PhysicalFile(path, "application/zip", name);
    }

    public IActionResult OnPostDelete(string name)
    {
        if (_backups.Delete(name))
        {
            Ok("🗑️ Το αντίγραφο σβήστηκε.");
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRestoreAsync(string name)
    {
        var path = _backups.PathOf(name);

        if (path == null)
        {
            Fail("Δεν βρέθηκε το αντίγραφο.");
            return RedirectToPage();
        }

        await using var stream = System.IO.File.OpenRead(path);
        return Result(await _backups.RestoreAsync(stream));
    }

    public async Task<IActionResult> OnPostUploadAsync(IFormFile? file)
    {
        if (file == null || file.Length == 0 || !file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            Fail("Διάλεξε το .zip του αντιγράφου.");
            return RedirectToPage();
        }

        await using var stream = file.OpenReadStream();
        return Result(await _backups.RestoreAsync(stream));
    }

    private IActionResult Result((bool Ok, string Message) result)
    {
        if (result.Ok)
        {
            Ok("✅ " + result.Message);
        }
        else
        {
            Fail(result.Message);
        }

        return RedirectToPage();
    }

    private void Ok(string message) { StatusKind = "success"; StatusMessage = message; }
    private void Fail(string message) { StatusKind = "danger"; StatusMessage = "⚠️ " + message; }
}

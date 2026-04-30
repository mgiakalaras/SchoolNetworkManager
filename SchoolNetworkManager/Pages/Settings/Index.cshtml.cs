using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;

namespace SchoolNetworkManager.Pages.Settings;

public class IndexModel : PageModel
{
    private readonly AppDbContext _context;
    private readonly IWebHostEnvironment _environment;

    public IndexModel(AppDbContext context, IWebHostEnvironment environment)
    {
        _context = context;
        _environment = environment;
    }

    [BindProperty]
    public AppSettings Settings { get; set; } = new();

    [BindProperty]
    public IFormFile? LogoUpload { get; set; }

    [BindProperty]
    public bool RemoveLogo { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var settings = await _context.AppSettings.FirstOrDefaultAsync(x => x.Id == 1);

        if (settings == null)
        {
            settings = new AppSettings
            {
                Id = 1,
                SchoolTitle = "School Network Manager",
                SchoolSubtitle = "Γρήγορη εικόνα του σχολικού δικτύου"
            };

            _context.AppSettings.Add(settings);
            await _context.SaveChangesAsync();
        }

        Settings = settings;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var existingSettings = await _context.AppSettings.FirstOrDefaultAsync(x => x.Id == 1);

        if (existingSettings == null)
        {
            existingSettings = new AppSettings { Id = 1 };
            _context.AppSettings.Add(existingSettings);
        }

        if (string.IsNullOrWhiteSpace(Settings.SchoolTitle))
        {
            ModelState.AddModelError("Settings.SchoolTitle", "Ο τίτλος είναι υποχρεωτικός.");
        }

        if (string.IsNullOrWhiteSpace(Settings.SchoolSubtitle))
        {
            ModelState.AddModelError("Settings.SchoolSubtitle", "Ο υπότιτλος είναι υποχρεωτικός.");
        }

        if (!ModelState.IsValid)
        {
            Settings.LogoPath = existingSettings.LogoPath;
            return Page();
        }

        existingSettings.SchoolTitle = Settings.SchoolTitle.Trim();
        existingSettings.SchoolSubtitle = Settings.SchoolSubtitle.Trim();

        if (RemoveLogo)
        {
            existingSettings.LogoPath = null;
        }

        if (LogoUpload != null && LogoUpload.Length > 0)
        {
            var allowedExtensions = new[] { ".png", ".jpg", ".jpeg", ".webp" };
            var extension = Path.GetExtension(LogoUpload.FileName).ToLowerInvariant();

            if (!allowedExtensions.Contains(extension))
            {
                ModelState.AddModelError("LogoUpload", "Το λογότυπο πρέπει να είναι PNG, JPG, JPEG ή WEBP.");
                Settings.LogoPath = existingSettings.LogoPath;
                return Page();
            }

            if (LogoUpload.Length > 2 * 1024 * 1024)
            {
                ModelState.AddModelError("LogoUpload", "Το λογότυπο δεν πρέπει να ξεπερνά τα 2MB.");
                Settings.LogoPath = existingSettings.LogoPath;
                return Page();
            }

            var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "settings");
            Directory.CreateDirectory(uploadsFolder);

            var fileName = $"logo_{Guid.NewGuid():N}{extension}";
            var filePath = Path.Combine(uploadsFolder, fileName);

            await using var stream = new FileStream(filePath, FileMode.Create);
            await LogoUpload.CopyToAsync(stream);

            existingSettings.LogoPath = $"/uploads/settings/{fileName}";
        }

        await _context.SaveChangesAsync();

        StatusMessage = "Οι ρυθμίσεις αποθηκεύτηκαν.";

        return RedirectToPage();
    }
}
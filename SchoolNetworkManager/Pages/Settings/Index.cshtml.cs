using System.Globalization;
using System.IO.Compression;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;
using SchoolNetworkManager.Services;

namespace SchoolNetworkManager.Pages.Settings;

public class IndexModel : PageModel
{
    private readonly AppDbContext _context;
    private readonly IWebHostEnvironment _environment;
    private readonly SettingsService _settingsService;

    public IndexModel(AppDbContext context, IWebHostEnvironment environment, SettingsService settingsService)
    {
        _context = context;
        _environment = environment;
        _settingsService = settingsService;
    }

    [BindProperty]
    public AppSettings Settings { get; set; } = new();

    [BindProperty]
    public IFormFile? LogoUpload { get; set; }

    [BindProperty]
    public bool RemoveLogo { get; set; }

    [BindProperty]
    public string? WeatherLatText { get; set; }

    [BindProperty]
    public string? WeatherLonText { get; set; }

    [BindProperty]
    public string? ActiveTab { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public int RangeCount { get; set; }

    public async Task<IActionResult> OnGetAsync(string? tab)
    {
        Settings = await LoadOrCreateAsync();
        ActiveTab = tab ?? "general";
        FillDisplayFields();

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var existing = await LoadOrCreateAsync();

        if (string.IsNullOrWhiteSpace(Settings.SchoolTitle))
        {
            ModelState.AddModelError("Settings.SchoolTitle", "Ο τίτλος είναι υποχρεωτικός.");
        }

        if (string.IsNullOrWhiteSpace(Settings.SchoolSubtitle))
        {
            ModelState.AddModelError("Settings.SchoolSubtitle", "Ο υπότιτλος είναι υποχρεωτικός.");
        }

        if (Settings.PageSize < 5 || Settings.PageSize > 500)
        {
            ModelState.AddModelError("Settings.PageSize", "Από 5 έως 500.");
        }

        if (Settings.MonitorIntervalMinutes < 0 || Settings.MonitorIntervalMinutes > 1440)
        {
            ModelState.AddModelError("Settings.MonitorIntervalMinutes", "Από 0 (απενεργοποίηση) έως 1440 λεπτά.");
        }

        if (!string.IsNullOrWhiteSpace(Settings.ScanRange) && NetworkTools.ParseRange(Settings.ScanRange).Count == 0)
        {
            ModelState.AddModelError("Settings.ScanRange", "Μη έγκυρο εύρος. Παράδειγμα: 192.168.2.1-254");
        }

        if (!string.IsNullOrWhiteSpace(Settings.WolBroadcast) && !IPAddress.TryParse(Settings.WolBroadcast.Trim(), out _))
        {
            ModelState.AddModelError("Settings.WolBroadcast", "Μη έγκυρη διεύθυνση. Παράδειγμα: 192.168.2.255");
        }

        if (!string.IsNullOrWhiteSpace(Settings.PortsToScan) && NetworkTools.ParsePorts(Settings.PortsToScan).Count == 0)
        {
            ModelState.AddModelError("Settings.PortsToScan", "Δεν βρέθηκε κανένα έγκυρο port. Παράδειγμα: 22:SSH, 3389:RDP");
        }

        if (!string.IsNullOrWhiteSpace(Settings.MySpeedUrl) &&
            (!Uri.TryCreate(Settings.MySpeedUrl.Trim(), UriKind.Absolute, out var mySpeedUri) ||
             (mySpeedUri.Scheme != Uri.UriSchemeHttp && mySpeedUri.Scheme != Uri.UriSchemeHttps)))
        {
            ModelState.AddModelError("Settings.MySpeedUrl", "Μη έγκυρη διεύθυνση. Παράδειγμα: http://127.0.0.1:5216");
        }

        var latitude = ParseCoordinate(WeatherLatText);
        var longitude = ParseCoordinate(WeatherLonText);

        if (Settings.ShowWeather && (latitude == null || longitude == null ||
            latitude < -90 || latitude > 90 || longitude < -180 || longitude > 180))
        {
            ModelState.AddModelError(nameof(WeatherLatText), "Δώσε έγκυρες συντεταγμένες (π.χ. 37.69 και 26.94).");
        }

        // Οι συντεταγμένες έρχονται από τα πεδία κειμένου, όχι από το Settings
        ModelState.Remove("Settings.WeatherLatitude");
        ModelState.Remove("Settings.WeatherLongitude");

        if (!ModelState.IsValid)
        {
            Settings.LogoPath = existing.LogoPath;
            FillDisplayFields(keepText: true);
            return Page();
        }

        existing.SchoolTitle = Settings.SchoolTitle.Trim();
        existing.SchoolSubtitle = Settings.SchoolSubtitle.Trim();
        existing.PageSize = Settings.PageSize;
        existing.NewBadgeDays = Math.Clamp(Settings.NewBadgeDays, 0, 365);
        existing.PublicFaultsPage = Settings.PublicFaultsPage;
        existing.PublicPortal = Settings.PublicPortal;
        existing.AutoBackup = Settings.AutoBackup;
        existing.AutoBackupHour = Math.Clamp(Settings.AutoBackupHour, 0, 23);
        existing.BackupKeep = Math.Clamp(Settings.BackupKeep, 1, 60);
        existing.CustomLinks = string.IsNullOrWhiteSpace(Settings.CustomLinks) ? null : Settings.CustomLinks.Trim();

        existing.DefaultTheme = Themes.Normalize(Settings.DefaultTheme);
        existing.EnableGlow = Settings.EnableGlow;
        existing.ShowWeather = Settings.ShowWeather;
        existing.WeatherPlace = Settings.WeatherPlace?.Trim();

        if (latitude != null && longitude != null)
        {
            existing.WeatherLatitude = latitude.Value;
            existing.WeatherLongitude = longitude.Value;
        }

        existing.ScanRange = Settings.ScanRange?.Trim();
        existing.MonitorIntervalMinutes = Settings.MonitorIntervalMinutes;
        existing.PortsToScan = string.IsNullOrWhiteSpace(Settings.PortsToScan)
            ? AppSettings.DefaultPorts
            : Settings.PortsToScan.Trim();
        existing.WolBroadcast = Settings.WolBroadcast?.Trim();

        // Οθόνες & RustDesk
        existing.ScreenWall = Settings.ScreenWall;
        existing.RustDeskApply = Settings.RustDeskApply;
        existing.RustDeskHost = string.IsNullOrWhiteSpace(Settings.RustDeskHost) ? null : Settings.RustDeskHost.Trim();
        existing.RustDeskKey = string.IsNullOrWhiteSpace(Settings.RustDeskKey) ? null : Settings.RustDeskKey.Trim();
        if (!string.IsNullOrWhiteSpace(Settings.RustDeskPassword))
        {
            existing.RustDeskPassword = Settings.RustDeskPassword.Trim() == "********"
                ? existing.RustDeskPassword
                : PasswordProtector.Protect(Settings.RustDeskPassword.Trim());
        }
        existing.MySpeedUrl = Settings.MySpeedUrl?.Trim();
        existing.PiholeUrl = Settings.PiholeUrl?.Trim();

        if (!string.IsNullOrWhiteSpace(Settings.PiholePassword))
        {
            existing.PiholePassword = Settings.PiholePassword.Trim();
        }

        // Ο κωδικός MySpeed δεν εμφανίζεται ξανά στη φόρμα· κενό πεδίο = κρατάμε τον παλιό.
        if (!string.IsNullOrWhiteSpace(Settings.MySpeedPassword))
        {
            existing.MySpeedPassword = Settings.MySpeedPassword.Trim();
        }

        if (RemoveLogo)
        {
            existing.LogoPath = null;
        }

        if (LogoUpload != null && LogoUpload.Length > 0)
        {
            var allowedExtensions = new[] { ".png", ".jpg", ".jpeg", ".webp" };
            var extension = Path.GetExtension(LogoUpload.FileName).ToLowerInvariant();

            if (!allowedExtensions.Contains(extension))
            {
                ModelState.AddModelError("LogoUpload", "Το λογότυπο πρέπει να είναι PNG, JPG, JPEG ή WEBP.");
                Settings.LogoPath = existing.LogoPath;
                FillDisplayFields(keepText: true);
                return Page();
            }

            if (LogoUpload.Length > 2 * 1024 * 1024)
            {
                ModelState.AddModelError("LogoUpload", "Το λογότυπο δεν πρέπει να ξεπερνά τα 2MB.");
                Settings.LogoPath = existing.LogoPath;
                FillDisplayFields(keepText: true);
                return Page();
            }

            var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "settings");
            Directory.CreateDirectory(uploadsFolder);

            var fileName = $"logo_{Guid.NewGuid():N}{extension}";
            var filePath = Path.Combine(uploadsFolder, fileName);

            await using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await LogoUpload.CopyToAsync(stream);
            }

            existing.LogoPath = $"/uploads/settings/{fileName}";
        }

        await _context.SaveChangesAsync();
        _settingsService.Update(existing);

        StatusMessage = "Οι ρυθμίσεις αποθηκεύτηκαν.";

        return RedirectToPage(new { tab = ActiveTab });
    }

    /// <summary>Κατεβάζει αντίγραφο ασφαλείας: βάση, κλειδιά κρυπτογράφησης και αρχεία (λογότυπο, κατόψεις, έγγραφα).</summary>
    public async Task<IActionResult> OnGetBackupAsync([FromServices] BackupService backups)
    {
        using var memory = new MemoryStream();
        await backups.WriteZipAsync(memory);

        var fileName = $"SchoolNetworkManager_backup_{DateTime.Now:yyyy-MM-dd_HH-mm}.zip";

        return File(memory.ToArray(), "application/zip", fileName);
    }

    private async Task<AppSettings> LoadOrCreateAsync()
    {
        var settings = await _context.AppSettings.FirstOrDefaultAsync(x => x.Id == 1);

        if (settings == null)
        {
            settings = new AppSettings { Id = 1 };
            _context.AppSettings.Add(settings);
            await _context.SaveChangesAsync();
        }

        return settings;
    }

    private void FillDisplayFields(bool keepText = false)
    {
        if (!keepText)
        {
            WeatherLatText = Settings.WeatherLatitude.ToString("0.####", CultureInfo.InvariantCulture);
            WeatherLonText = Settings.WeatherLongitude.ToString("0.####", CultureInfo.InvariantCulture);
        }

        RangeCount = NetworkTools.ParseRange(Settings.ScanRange).Count;

        if (!keepText && !string.IsNullOrWhiteSpace(Settings.RustDeskPassword))
        {
            Settings.RustDeskPassword = "********";
        }
    }

    private static double? ParseCoordinate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalized = text.Trim().Replace(',', '.');

        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }
}

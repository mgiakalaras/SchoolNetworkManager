using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;
using SchoolNetworkManager.Services;

namespace SchoolNetworkManager.Pages.Agent;

/// <summary>Agent υπολογιστών: εγκατάσταση, ποιοι στέλνουν στοιχεία, αναζήτηση λογισμικού.</summary>
public class IndexModel : PageModel
{
    private readonly AppDbContext _context;
    private readonly SettingsService _settings;

    public IndexModel(AppDbContext context, SettingsService settings)
    {
        _context = context;
        _settings = settings;
    }

    [BindProperty(SupportsGet = true)]
    public string? Tab { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    /// <summary>Διεύθυνση του server όπως θα τη γράψει ο agent (προεπιλογή: αυτή που βλέπει τώρα ο browser).</summary>
    [BindProperty(SupportsGet = true)]
    public string? ServerUrl { get; set; }

    public string AgentKey => _settings.Current.AgentKey ?? string.Empty;

    public string ScreenKey => _settings.Current.ScreenKey ?? string.Empty;

    /// <summary>Διευθύνσεις του server που μπορούν να δουν οι υπολογιστές (η πρώτη είναι η προτεινόμενη).</summary>
    public List<string> ServerChoices { get; set; } = new();

    /// <summary>
    /// Ο browser μπορεί να βλέπει τον server από VPN / απομακρυσμένη διεύθυνση (π.χ. 10.242.x.x του ZimaOS).
    /// Οι υπολογιστές του σχολείου όμως χρειάζονται τη διεύθυνση του σχολικού δικτύου — προτιμάμε αυτή του εύρους σάρωσης.
    /// </summary>
    private List<string> SuggestServerUrls()
    {
        var port = Request.Host.Port ?? 5055;
        var scanPrefix = NetworkTools.ParseRange(_settings.Current.ScanRange, 4).FirstOrDefault() is { } first
            ? string.Join('.', first.Split('.').Take(3)) + "."
            : "192.168.";

        var addresses = new List<string>();

        try
        {
            addresses = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(a => a.Address)
                .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(a))
                .Select(a => a.ToString())
                .Where(ip => !ip.StartsWith("172.17.") && !ip.StartsWith("169.254."))
                .Distinct()
                .ToList();
        }
        catch (Exception)
        {
        }

        var ordered = addresses
            .OrderByDescending(ip => ip.StartsWith(scanPrefix))
            .ThenByDescending(ip => ip.StartsWith("192.168."))
            .Select(ip => $"http://{ip}:{port}")
            .ToList();

        var current = $"{Request.Scheme}://{Request.Host}";

        if (!ordered.Contains(current))
        {
            ordered.Add(current);
        }

        return ordered;
    }

    public List<Row> Rows { get; set; } = new();
    public List<SoftwareGroup> Software { get; set; } = new();
    public List<NewSoftware> Recent { get; set; } = new();
    public int TotalPrograms { get; set; }

    [TempData] public string? StatusMessage { get; set; }

    public class Row
    {
        public AgentReport Report { get; set; } = new();
        public Computer? Device { get; set; }
        public int NewPrograms { get; set; }
    }

    public class SoftwareGroup
    {
        public string Name { get; set; } = string.Empty;
        public string? Publisher { get; set; }
        public List<string> Versions { get; set; } = new();
        public List<(int Id, string Label, string? Version, bool IsNew, string? Scope)> Devices { get; set; } = new();
        public bool AnyUser => Devices.Any(d => d.Scope is "user" or "store");
    }

    public class NewSoftware
    {
        public InstalledSoftware Item { get; set; } = new();
        public Computer? Device { get; set; }
    }

    public async Task OnGetAsync()
    {
        ServerChoices = SuggestServerUrls();
        ServerUrl ??= ServerChoices.First();

        var reports = await _context.AgentReports.AsNoTracking().ToListAsync();
        var devices = await _context.Computers.AsNoTracking().ToDictionaryAsync(c => c.Id);
        var software = await _context.InstalledSoftware.AsNoTracking().ToListAsync();
        var reportBy = reports.ToDictionary(r => r.ComputerId);

        bool IsNew(InstalledSoftware s) => AgentIngest.IsNew(s, reportBy.GetValueOrDefault(s.ComputerId));

        Rows = reports
            .Select(r => new Row
            {
                Report = r,
                Device = devices.GetValueOrDefault(r.ComputerId),
                NewPrograms = software.Count(s => s.ComputerId == r.ComputerId && IsNew(s) && s.FirstSeen > DateTime.Now.AddDays(-14))
            })
            .OrderBy(r => r.Device?.Location ?? "~")
            .ThenBy(r => r.Device == null ? long.MaxValue : NetworkTools.IpToNumber(r.Device.IpAddress))
            .ToList();

        string Label(int id) => devices.TryGetValue(id, out var d) ? (string.IsNullOrWhiteSpace(d.Name) ? d.IpAddress : d.Name!) : $"#{id}";

        Recent = software
            .Where(s => IsNew(s) && s.FirstSeen > DateTime.Now.AddDays(-14))
            .OrderByDescending(s => s.FirstSeen)
            .Take(100)
            .Select(s => new NewSoftware { Item = s, Device = devices.GetValueOrDefault(s.ComputerId) })
            .ToList();

        var term = Q?.Trim();
        var filtered = string.IsNullOrEmpty(term)
            ? software
            : software.Where(s => s.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                                  (s.Publisher ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();

        TotalPrograms = software.Select(s => s.Name.ToLowerInvariant()).Distinct().Count();

        Software = filtered
            .GroupBy(s => s.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new SoftwareGroup
            {
                Name = g.Key,
                Publisher = g.Select(s => s.Publisher).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)),
                Versions = g.Select(s => s.Version).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().OrderBy(v => v).Select(v => v!).ToList(),
                Devices = g.Select(s => (s.ComputerId, Label(s.ComputerId), s.Version, IsNew(s), s.Scope))
                    .DistinctBy(x => x.ComputerId)
                    .OrderBy(x => x.Item2)
                    .ToList()
            })
            .OrderByDescending(g => g.Devices.Count)
            .ThenBy(g => g.Name)
            .ToList();
    }

    /// <summary>Πακέτο εγκατάστασης για τους υπολογιστές, με έτοιμη διεύθυνση server και κλειδί.</summary>
    public IActionResult OnGetDownload(string? serverUrl)
    {
        var server = string.IsNullOrWhiteSpace(serverUrl) ? SuggestServerUrls().First() : serverUrl.Trim().TrimEnd('/');
        var folder = Path.Combine(AppContext.BaseDirectory, "Resources", "Agent");

        using var memory = new MemoryStream();

        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in new[] { "snm-agent.ps1", "snm-screen.ps1", "install.ps1", "install.bat", "uninstall.ps1", "uninstall.bat" })
            {
                var path = Path.Combine(folder, file);

                if (System.IO.File.Exists(path))
                {
                    zip.CreateEntryFromFile(path, "SNM-Agent/" + file);
                }
            }

            AddText(zip, "SNM-Agent/config.json", JsonSerializer.Serialize(new { server, key = AgentKey }, new JsonSerializerOptions { WriteIndented = true }));
            AddText(zip, "SNM-Agent/screen.json", JsonSerializer.Serialize(new { server, screenKey = ScreenKey }, new JsonSerializerOptions { WriteIndented = true }));
            AddText(zip, "SNM-Agent/ΔΙΑΒΑΣΕ-ΜΕ.txt", Readme(server), bom: true);
        }

        return File(memory.ToArray(), "application/zip", "SNM-Agent.zip");
    }

    public async Task<IActionResult> OnPostNewKeyAsync()
    {
        if (!UserPasswords.IsAdmin(User) && User.Identity?.IsAuthenticated == true)
        {
            StatusMessage = "⚠️ Μόνο διαχειριστής αλλάζει το κλειδί.";
            return RedirectToPage(new { Tab = "setup" });
        }

        var settings = await _context.AppSettings.FirstAsync(x => x.Id == 1);
        settings.AgentKey = AgentIngest.NewKey();
        await _context.SaveChangesAsync();
        _settings.Update(settings);

        StatusMessage = "🔑 Νέο κλειδί. Οι agents που είναι ήδη εγκατεστημένοι σταματούν — κατέβασε ξανά το πακέτο και ξαναεγκατάστησέ το.";
        return RedirectToPage(new { Tab = "setup" });
    }

    public async Task<IActionResult> OnPostForgetAsync(int computerId)
    {
        await _context.AgentReports.Where(r => r.ComputerId == computerId).ExecuteDeleteAsync();
        await _context.InstalledSoftware.Where(s => s.ComputerId == computerId).ExecuteDeleteAsync();
        StatusMessage = "Τα στοιχεία του agent για αυτή τη συσκευή σβήστηκαν (αν ο agent είναι ακόμα εγκατεστημένος, θα ξαναστείλει).";
        return RedirectToPage();
    }

    private static void AddText(ZipArchive zip, string name, string text, bool bom = false)
    {
        var entry = zip.CreateEntry(name);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(bom));
        writer.Write(text.Replace("\n", "\r\n"));
    }

    private static string Readme(string server) => $"""
School Network Manager — Agent υπολογιστών
==========================================

Τι κάνει
--------
Κάθε 2 ώρες, στην εκκίνηση και σε κάθε σύνδεση χρήστη στέλνει στο {server}:
όνομα υπολογιστή, IP, MAC, επεξεργαστή, μνήμη, δίσκους (και πόσο γεμάτοι είναι),
κάρτα γραφικών, Windows, κατασκευαστή / μοντέλο / σειριακό, αν τρέχει το Veyon
και τη λίστα με τα εγκατεστημένα προγράμματα.
Μόνο διαβάζει — δεν αλλάζει τίποτα στον υπολογιστή.

Εγκατάσταση (σε κάθε υπολογιστή, μία φορά)
-------------------------------------------
1. Αντίγραψε τον φάκελο SNM-Agent στον υπολογιστή (ή άνοιξέ τον από USB / κοινόχρηστο φάκελο).
2. Δεξί κλικ στο install.bat → «Εκτέλεση ως διαχειριστής».
3. Σε λίγα δευτερόλεπτα ο υπολογιστής εμφανίζεται στο School Network Manager.

Αθόρυβα (π.χ. από script):  install.bat /quiet

Πού μπαίνει
-----------
C:\ProgramData\SchoolNetworkManager\Agent  (μόνο οι διαχειριστές έχουν πρόσβαση)
Προγραμματισμένη εργασία: «SchoolNetworkManager Agent» (τρέχει ως SYSTEM)
Ημερολόγιο: C:\ProgramData\SchoolNetworkManager\Agent\agent.log

Απεγκατάσταση
-------------
Δεξί κλικ στο uninstall.bat → «Εκτέλεση ως διαχειριστής».

Ενημερώσεις
-----------
Ο agent ενημερώνεται μόνος του όταν βγαίνει νέα έκδοση στον server.

⚠ Το config.json περιέχει το κλειδί του σχολείου. Μην το αφήνεις σε φάκελο που βλέπουν οι μαθητές.
""";
}

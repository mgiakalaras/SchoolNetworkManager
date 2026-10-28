using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;
using SchoolNetworkManager.Services;

namespace SchoolNetworkManager.Pages.Computers;

public class DetailsModel : PageModel
{
    private readonly AppDbContext _context;
    private readonly DeviceProbe _probe;
    private readonly SettingsService _settings;
    private readonly OuiLookup _oui;

    public DetailsModel(AppDbContext context, DeviceProbe probe, SettingsService settings, OuiLookup oui)
    {
        _context = context;
        _probe = probe;
        _settings = settings;
        _oui = oui;
    }

    public Computer Computer { get; set; } = new();

    public UsageHistory History { get; set; } = new();

    public List<Fault> Faults { get; set; } = new();

    public List<PasswordHistoryItem> PasswordHistory { get; set; } = new();

    /// <summary>Η τελευταία αναφορά του agent (αν είναι εγκατεστημένος).</summary>
    public AgentReport? Agent { get; set; }

    public List<InstalledSoftware> Software { get; set; } = new();

    public List<(string Drive, string? Label, double SizeGb, double FreeGb)> Disks { get; set; } = new();

    public class PasswordHistoryItem
    {
        public PasswordRecord Record { get; set; } = new();
        public bool IsCurrent { get; set; }
    }

    [BindProperty]
    public Fault NewFault { get; set; } = new();

    [BindProperty]
    public string? ResolutionText { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? StatusKind { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var computer = await _context.Computers.FindAsync(id);

        if (computer == null)
        {
            return NotFound();
        }

        Computer = computer;
        await LoadExtrasAsync();

        return Page();
    }

    private async Task LoadExtrasAsync()
    {
        Faults = await _context.Faults
            .Where(f => f.ComputerId == Computer.Id)
            .OrderBy(f => f.ResolvedAt != null)
            .ThenByDescending(f => f.CreatedAt)
            .Take(30)
            .ToListAsync();

        var passwords = await _context.PasswordRecords
            .Where(p => p.ComputerId == Computer.Id)
            .OrderByDescending(p => p.SetAt)
            .Take(20)
            .ToListAsync();

        var currentIds = passwords
            .GroupBy(p => p.Account)
            .Select(g => g.First().Id)
            .ToHashSet();

        PasswordHistory = passwords
            .Take(6)
            .Select(p => new PasswordHistoryItem { Record = p, IsCurrent = currentIds.Contains(p.Id) })
            .ToList();

        var id = Computer.Id;
        Agent = await _context.AgentReports.AsNoTracking().FirstOrDefaultAsync(r => r.ComputerId == id);

        if (Agent != null)
        {
            Software = (await _context.InstalledSoftware.AsNoTracking().Where(s => s.ComputerId == id).ToListAsync())
                .OrderByDescending(s => SchoolNetworkManager.Services.AgentIngest.IsNew(s, Agent))
                .ThenBy(s => s.Name)
                .ToList();

            try
            {
                using var json = System.Text.Json.JsonDocument.Parse(Agent.DisksJson ?? "[]");

                foreach (var d in json.RootElement.EnumerateArray())
                {
                    Disks.Add((
                        d.TryGetProperty("Drive", out var dr) ? dr.GetString() ?? "" : "",
                        d.TryGetProperty("Label", out var lb) ? lb.GetString() : null,
                        d.TryGetProperty("SizeGb", out var sz) ? sz.GetDouble() : 0,
                        d.TryGetProperty("FreeGb", out var fr) ? fr.GetDouble() : 0));
                }
            }
            catch (System.Text.Json.JsonException)
            {
            }
        }

        const int days = 14;
        var today = DateTime.Now.Date;
        var from = today.AddDays(-(days - 1));

        var rows = await _context.DeviceHours
            .Where(h => h.ComputerId == Computer.Id && h.Hour >= from)
            .ToListAsync();

        var byHour = rows.ToDictionary(h => h.Hour);

        for (var day = from; day <= today; day = day.AddDays(1))
        {
            var cells = new List<UsageCell>();

            for (var h = 0; h < 24; h++)
            {
                byHour.TryGetValue(day.AddHours(h), out var row);
                cells.Add(new UsageCell
                {
                    Hour = h,
                    Ratio = row == null || row.Checks == 0 ? null : (double)row.OnlineChecks / row.Checks,
                    Drops = row?.Drops ?? 0
                });
            }

            History.Days.Add(new UsageDay { Date = day, Cells = cells });
        }

        History.HasData = rows.Count > 0;
        History.TotalChecks = rows.Sum(r => r.Checks);
        History.OnlineChecks = rows.Sum(r => r.OnlineChecks);
        History.Drops = rows.Sum(r => r.Drops);
        History.UsedDays = rows.Where(r => r.OnlineChecks > 0).Select(r => r.Hour.Date).Distinct().Count();
        History.TrackedDays = rows.Select(r => r.Hour.Date).Distinct().Count();
        History.DayCount = days;
    }

    public async Task<IActionResult> OnPostReportFaultAsync(int id)
    {
        var computer = await _context.Computers.FindAsync(id);

        if (computer == null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(NewFault.Description))
        {
            StatusKind = "warning";
            StatusMessage = "Γράψε τι πρόβλημα έχει η συσκευή.";
            return RedirectToPage(new { id });
        }

        _context.Faults.Add(new Fault
        {
            ComputerId = id,
            Description = NewFault.Description.Trim(),
            Severity = Fault.Severities.Contains(NewFault.Severity) ? NewFault.Severity : Fault.SeverityMinor,
            ReportedBy = string.IsNullOrWhiteSpace(NewFault.ReportedBy) ? null : NewFault.ReportedBy.Trim(),
            CreatedBy = SchoolNetworkManager.Services.UserPasswords.ActorName(User),
            CreatedAt = DateTime.Now
        });

        computer.UpdatedAt = DateTime.Now;
        await _context.SaveChangesAsync();

        StatusKind = "warning";
        StatusMessage = "🔧 Η βλάβη καταχωρήθηκε. Η συσκευή φαίνεται με 🔧 μέχρι να κλείσει η βλάβη.";

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostResolveFaultAsync(int id, int faultId)
    {
        var fault = await _context.Faults.FirstOrDefaultAsync(f => f.Id == faultId && f.ComputerId == id);

        if (fault != null && fault.ResolvedAt == null)
        {
            fault.ResolvedAt = DateTime.Now;
            fault.Resolution = string.IsNullOrWhiteSpace(ResolutionText) ? null : ResolutionText.Trim();
            fault.ResolvedBy = SchoolNetworkManager.Services.UserPasswords.ActorName(User);
            await _context.SaveChangesAsync();

            StatusKind = "success";
            StatusMessage = "✅ Η βλάβη έκλεισε.";
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostReopenFaultAsync(int id, int faultId)
    {
        var fault = await _context.Faults.FirstOrDefaultAsync(f => f.Id == faultId && f.ComputerId == id);

        if (fault != null)
        {
            fault.ResolvedAt = null;
            fault.ResolvedBy = null;
            await _context.SaveChangesAsync();
        }

        return RedirectToPage(new { id });
    }

    public class UsageCell
    {
        public int Hour { get; set; }
        public double? Ratio { get; set; }
        public int Drops { get; set; }

        /// <summary>Κλάση χρώματος: hn = χωρίς έλεγχο, h0 = κλειστό, h1–h4 = όλο και περισσότερο ανοιχτό.</summary>
        public string Css => Ratio == null ? "hn"
            : Ratio <= 0 ? "h0"
            : Ratio < 0.34 ? "h1"
            : Ratio < 0.67 ? "h2"
            : Ratio < 1 ? "h3"
            : "h4";
    }

    public class UsageDay
    {
        public DateTime Date { get; set; }
        public List<UsageCell> Cells { get; set; } = new();
    }

    public class UsageHistory
    {
        public bool HasData { get; set; }
        public List<UsageDay> Days { get; set; } = new();
        public int TotalChecks { get; set; }
        public int OnlineChecks { get; set; }
        public int Drops { get; set; }
        public int UsedDays { get; set; }
        public int TrackedDays { get; set; }
        public int DayCount { get; set; }

        public double OnlinePercent => TotalChecks == 0 ? 0 : OnlineChecks * 100.0 / TotalChecks;

        /// <summary>Πολλά ανοιγοκλεισίματα ανά ημέρα χρήσης → πιθανό πρόβλημα (ρεύμα, καλώδιο, κάρτα δικτύου).</summary>
        public bool IsFlapping => UsedDays > 0 && Drops >= 4 && (double)Drops / UsedDays > 2.5;

        public bool NeverOn => TrackedDays >= 3 && OnlineChecks == 0;
    }

    public async Task<IActionResult> OnPostProbeAsync(int id)
    {
        var computer = await _context.Computers.FindAsync(id);

        if (computer == null)
        {
            return NotFound();
        }

        var result = await _probe.ProbeAsync(computer.IpAddress);
        var changes = new List<string>();
        var now = DateTime.Now;

        computer.IsOnline = result.Online;
        computer.LastCheckedAt = now;

        if (result.Online)
        {
            computer.LastSeenOnline = now;
            computer.OpenPorts = result.OpenPortsText;

            if (!string.IsNullOrWhiteSpace(result.Name) && string.IsNullOrWhiteSpace(computer.Name))
            {
                computer.Name = result.Name;
                changes.Add($"όνομα {result.Name}");
            }

            if (!string.IsNullOrWhiteSpace(result.Mac))
            {
                if (string.IsNullOrWhiteSpace(computer.MacAddress))
                {
                    computer.MacAddress = result.Mac;
                    changes.Add($"MAC {result.Mac}");
                }

                computer.DetectedMac = result.Mac;
            }

            if (!string.IsNullOrWhiteSpace(result.Vendor))
            {
                computer.Vendor = result.Vendor;
            }

            // Φύλακας Veyon και από την χειροκίνητη ανίχνευση
            if (computer.HasVeyon && result.PortsChecked)
            {
                if (result.HasVeyon)
                {
                    computer.VeyonDown = false;
                    computer.VeyonDownSince = null;
                }
                else if (!computer.VeyonDown)
                {
                    computer.VeyonDown = true;
                    computer.VeyonDownSince = now;
                }
            }

            if (result.HasVeyon && !computer.HasVeyon)
            {
                computer.HasVeyon = true;
                changes.Add("Veyon");
            }

            if (result.SuggestedType != null && computer.MachineType == "Άγνωστο")
            {
                computer.MachineType = result.SuggestedType;
                changes.Add($"τύπος {result.SuggestedType}");
            }

            if (changes.Count > 0)
            {
                computer.UpdatedAt = now;
            }
        }

        await _context.SaveChangesAsync();

        if (!result.Online)
        {
            StatusKind = "warning";
            StatusMessage = "Η συσκευή δεν απάντησε. Ίσως είναι κλειστή — δοκίμασε Wake-on-LAN.";
        }
        else
        {
            StatusKind = "success";
            StatusMessage = changes.Count > 0
                ? "Η ανίχνευση ολοκληρώθηκε. Συμπληρώθηκαν: " + string.Join(", ", changes) + "."
                : "Η ανίχνευση ολοκληρώθηκε. Τα στοιχεία ενημερώθηκαν.";
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostWakeAsync(int id)
    {
        var computer = await _context.Computers.FindAsync(id);

        if (computer == null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(computer.MacAddress))
        {
            StatusKind = "warning";
            StatusMessage = "Χρειάζεται MAC Address για το Wake-on-LAN. Πάτα πρώτα «Ανίχνευση» όσο η συσκευή είναι ανοιχτή.";
            return RedirectToPage(new { id });
        }

        var sent = await NetworkTools.SendWakeOnLanAsync(computer.MacAddress, _settings.Current.WolBroadcast);

        StatusKind = sent ? "success" : "danger";
        StatusMessage = sent
            ? "Στάλθηκε σήμα αφύπνισης. Ο υπολογιστής θα ανοίξει σε λίγο (αν έχει ενεργό το Wake-on-LAN στο BIOS)."
            : "Δεν στάλθηκε το σήμα αφύπνισης. Έλεγξε τη MAC Address.";

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostAcceptMacAsync(int id)
    {
        var computer = await _context.Computers.FindAsync(id);

        if (computer == null)
        {
            return NotFound();
        }

        if (!string.IsNullOrWhiteSpace(computer.DetectedMac))
        {
            computer.MacAddress = computer.DetectedMac;
            computer.Vendor = _oui.Lookup(computer.DetectedMac) ?? computer.Vendor;
            computer.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync();

            StatusKind = "success";
            StatusMessage = "Η νέα MAC αποθηκεύτηκε.";
        }

        return RedirectToPage(new { id });
    }
}

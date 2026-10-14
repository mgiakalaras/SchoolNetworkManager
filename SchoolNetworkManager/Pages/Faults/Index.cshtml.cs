using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;
using SchoolNetworkManager.Services;

namespace SchoolNetworkManager.Pages.Faults;

public class IndexModel : PageModel
{
    private readonly AppDbContext _context;
    private readonly SettingsService _settings;

    public IndexModel(AppDbContext context, SettingsService settings)
    {
        _context = context;
        _settings = settings;
    }

    public List<FaultRow> Open { get; set; } = new();

    public List<FaultRow> RecentlyClosed { get; set; } = new();

    public bool PublicEnabled => _settings.Current.PublicFaultsPage;

    public bool PortalEnabled => _settings.Current.PublicPortal;

    /// <summary>Δηλώσεις από τη δημόσια φόρμα που περιμένουν έλεγχο.</summary>
    public List<FaultReport> Pending { get; set; } = new();

    /// <summary>Συσκευές για την επιλογή στην αποδοχή (ανά χώρο).</summary>
    public List<IGrouping<string, Computer>> DeviceGroups { get; set; } = new();

    public Dictionary<int, Computer> DevicesById { get; set; } = new();

    public HashSet<int> HasOpenFault { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync()
    {
        var rows = await FaultQueries.LoadAsync(_context, includeClosedDays: 30);

        Open = rows.Where(r => r.Fault.IsOpen).ToList();
        RecentlyClosed = rows.Where(r => !r.Fault.IsOpen).OrderByDescending(r => r.Fault.ResolvedAt).ToList();
        HasOpenFault = Open.Select(r => r.Fault.ComputerId).ToHashSet();

        Pending = await _context.FaultReports
            .Where(r => r.Status == FaultReport.StatusNew)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        if (Pending.Count > 0)
        {
            var devices = (await _context.Computers.AsNoTracking().ToListAsync())
                .Where(c => !c.IsEmptySlot)
                .ToList();

            DevicesById = devices.ToDictionary(c => c.Id);
            DeviceGroups = devices
                .OrderBy(c => NetworkTools.IpToNumber(c.IpAddress))
                .GroupBy(c => string.IsNullOrWhiteSpace(c.Location) ? "Χωρίς χώρο" : c.Location.Trim())
                .OrderBy(g => g.Key)
                .ToList();
        }
    }

    /// <summary>Η δήλωση γίνεται κανονική βλάβη στο βιβλίο.</summary>
    public async Task<IActionResult> OnPostAcceptReportAsync(int reportId, int? computerId, string? severity, string? description)
    {
        var report = await _context.FaultReports.FindAsync(reportId);

        if (report == null || report.Status != FaultReport.StatusNew)
        {
            return RedirectToPage();
        }

        var deviceId = computerId ?? report.ComputerId;

        if (deviceId == null || !await _context.Computers.AnyAsync(c => c.Id == deviceId))
        {
            StatusMessage = "⚠️ Διάλεξε σε ποια συσκευή αντιστοιχεί η δήλωση.";
            return RedirectToPage();
        }

        var text = string.IsNullOrWhiteSpace(description) ? report.Description : description.Trim();

        if (!string.IsNullOrWhiteSpace(report.DeviceText) && report.ComputerId == null)
        {
            text += $" ({report.DeviceText})";
        }

        var actor = UserPasswords.ActorName(User);
        var fault = new Fault
        {
            ComputerId = deviceId.Value,
            Description = text.Length > 1000 ? text[..1000] : text,
            Severity = Fault.Severities.Contains(severity) ? severity! : report.Severity,
            ReportedBy = string.IsNullOrWhiteSpace(report.ReporterLabel) ? "Δημόσια φόρμα" : $"{report.ReporterLabel} (φόρμα)",
            CreatedBy = actor,
            CreatedAt = report.CreatedAt
        };

        _context.Faults.Add(fault);
        await _context.SaveChangesAsync();

        report.Status = FaultReport.StatusAccepted;
        report.ComputerId = deviceId;
        report.FaultId = fault.Id;
        report.HandledBy = actor;
        report.HandledAt = DateTime.Now;
        await _context.SaveChangesAsync();

        StatusMessage = "✅ Η δήλωση μπήκε στο βιβλίο βλαβών.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRejectReportAsync(int reportId)
    {
        var report = await _context.FaultReports.FindAsync(reportId);

        if (report != null && report.Status == FaultReport.StatusNew)
        {
            report.Status = FaultReport.StatusRejected;
            report.HandledBy = UserPasswords.ActorName(User);
            report.HandledAt = DateTime.Now;
            await _context.SaveChangesAsync();
            StatusMessage = "Η δήλωση απορρίφθηκε.";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostResolveAsync(int faultId, string? resolution)
    {
        var fault = await _context.Faults.FindAsync(faultId);

        if (fault != null && fault.ResolvedAt == null)
        {
            fault.ResolvedAt = DateTime.Now;
            fault.Resolution = string.IsNullOrWhiteSpace(resolution) ? null : resolution.Trim();
            fault.ResolvedBy = SchoolNetworkManager.Services.UserPasswords.ActorName(User);
            await _context.SaveChangesAsync();
            StatusMessage = "✅ Η βλάβη έκλεισε.";
        }

        return RedirectToPage();
    }
}

public static class FaultQueries
{
    /// <summary>Ανοιχτές βλάβες + όσες έκλεισαν τις τελευταίες Χ ημέρες, μαζί με τη συσκευή τους.</summary>
    public static async Task<List<FaultRow>> LoadAsync(AppDbContext db, int includeClosedDays)
    {
        var since = DateTime.Now.AddDays(-includeClosedDays);

        var faults = await db.Faults
            .Where(f => f.ResolvedAt == null || f.ResolvedAt >= since)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync();

        var ids = faults.Select(f => f.ComputerId).Distinct().ToList();
        var computers = await db.Computers
            .AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id);

        return faults
            .Select(f => new FaultRow { Fault = f, Computer = computers.GetValueOrDefault(f.ComputerId) })
            .OrderBy(r => r.LocationLabel)
            .ThenBy(r => NetworkTools.IpToNumber(r.Computer?.IpAddress))
            .ToList();
    }
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;
using System.Globalization;
using SchoolNetworkManager.Services;

namespace SchoolNetworkManager.Pages;

public class IndexModel : PageModel
{
    private readonly AppDbContext _context;
    private readonly MySpeedClient _mySpeed;
    private readonly PiholeClient _pihole;

    public IndexModel(AppDbContext context, MySpeedClient mySpeed, PiholeClient pihole)
    {
        _context = context;
        _mySpeed = mySpeed;
        _pihole = pihole;
    }

    public PiholeStats? Pihole { get; set; }

    /// <summary>IP → συσκευή, για να φαίνονται ονόματα αντί για IP στα στατιστικά του Pi-hole.</summary>
    public Dictionary<string, Computer> DevicesByIp { get; set; } = new();

    public int OpenFaults { get; set; }

    /// <summary>Ανοιχτοί υπολογιστές όπου κάποιος έκλεισε το Veyon.</summary>
    public List<Computer> VeyonDownDevices { get; set; } = new();
    public List<Consumable> LowStock { get; set; } = new();
    public int PendingReports { get; set; }

    public int DownFaults { get; set; }

    private static readonly CultureInfo Greek = new("el-GR");

    public WeekChart Week { get; set; } = new();

    public SpeedChart Speed { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public int TotalComputers { get; set; }
    public int DynamicAddresses { get; set; }
    public int DynamicInUse { get; set; }
    public int OnlineComputers { get; set; }
    public int OfflineComputers { get; set; }
    public int UnknownStatusComputers { get; set; }
    public int VeyonComputers { get; set; }
    public int LanMessengerComputers { get; set; }

    public AppSettings Settings { get; set; } = new()
    {
        SchoolTitle = "School Network Manager",
        SchoolSubtitle = "Γρήγορη εικόνα του σχολικού δικτύου"
    };

    public List<LocationSummary> Locations { get; set; } = new();
    public List<MachineTypeSummary> MachineTypeStats { get; set; } = new();
    public List<Computer> LatestComputers { get; set; } = new();

    public async Task OnGetAsync()
    {
        await LoadDashboardAsync();
        await LoadWeekAsync();

        // Pi-hole και MySpeed παράλληλα, για να ανοίγει γρήγορα η σελίδα
        Task<PiholeStats>? piholeTask = _pihole.IsConfigured ? _pihole.GetStatsAsync() : null;
        await LoadSpeedAsync();
        Pihole = piholeTask == null ? null : await piholeTask;

        var openFaults = await _context.Faults.Where(f => f.ResolvedAt == null).Select(f => f.Severity).ToListAsync();
        OpenFaults = openFaults.Count;
        DownFaults = openFaults.Count(s => s == Fault.SeverityDown);
    }

    // ---------- Γράφημα: μέγιστες online συσκευές ανά ημέρα ----------

    private async Task LoadWeekAsync()
    {
        var today = DateTime.Now.Date;
        var from = today.AddDays(-6);

        var snapshots = await _context.StatusSnapshots
            .Where(x => x.Timestamp >= from)
            .OrderBy(x => x.Timestamp)
            .ToListAsync();

        Week.HasData = snapshots.Count > 0;

        for (var day = from; day <= today; day = day.AddDays(1))
        {
            var current = day;
            var ofDay = snapshots.Where(x => x.Timestamp.Date == current).ToList();
            var peak = ofDay.OrderByDescending(x => x.Online).ThenBy(x => x.Timestamp).FirstOrDefault();

            Week.Days.Add(new DayPoint
            {
                Date = current,
                Label = Greek.DateTimeFormat.GetAbbreviatedDayName(current.DayOfWeek),
                ShortDate = current.ToString("d/M"),
                Peak = peak?.Online,
                PeakTime = peak?.Timestamp,
                Total = peak?.Total,
                Average = ofDay.Count == 0 ? null : ofDay.Average(x => x.Online),
                Samples = ofDay.Count
            });
        }

        var best = Week.Days.Where(d => d.Peak != null).OrderByDescending(d => d.Peak).ThenBy(d => d.Date).FirstOrDefault();

        if (best != null)
        {
            best.IsPeak = true;
            Week.Best = best;
        }

        (Week.AxisMax, Week.AxisStep) = NiceAxis(Week.Days.Max(d => (double?)d.Peak) ?? 0, minimum: 4);
    }

    // ---------- Γράφημα: ταχύτητα δικτύου από το MySpeed ----------

    private async Task LoadSpeedAsync()
    {
        var result = await _mySpeed.GetLastDaysAsync(7);

        Speed.Error = result.Ok ? null : result.Error;
        Speed.Tests = result.Tests;
        Speed.From = DateTime.Now.Date.AddDays(-6);
        Speed.To = DateTime.Now.Date.AddDays(1);

        if (Speed.Tests.Count == 0)
        {
            return;
        }

        var downloads = Speed.Tests.Where(t => t.Download != null).Select(t => t.Download!.Value).ToList();
        var uploads = Speed.Tests.Where(t => t.Upload != null).Select(t => t.Upload!.Value).ToList();
        var pings = Speed.Tests.Where(t => t.Ping != null).Select(t => t.Ping!.Value).ToList();

        Speed.AvgDownload = downloads.Count > 0 ? downloads.Average() : null;
        Speed.AvgUpload = uploads.Count > 0 ? uploads.Average() : null;
        Speed.AvgPing = pings.Count > 0 ? pings.Average() : null;
        Speed.MinDownload = downloads.Count > 0 ? Speed.Tests.Where(t => t.Download != null).MinBy(t => t.Download) : null;
        (Speed.AxisMax, Speed.AxisStep) = NiceAxis(Math.Max(downloads.DefaultIfEmpty(0).Max(), uploads.DefaultIfEmpty(0).Max()), minimum: 10);
    }

    /// <summary>
    /// Στρογγυλός άξονας με 3–5 γραμμές: επιστρέφει μέγιστο και βήμα (π.χ. 23 → 30 ανά 10, 108 → 150 ανά 50).
    /// </summary>
    public static (double Max, double Step) NiceAxis(double value, double minimum)
    {
        value = Math.Max(value, minimum);

        var rough = value / 4;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        var normalized = rough / magnitude;

        var step = (normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10) * magnitude;
        step = Math.Max(step, 1);

        return (Math.Ceiling(value / step) * step, step);
    }

    public static string Num(double value, string format = "0.#") =>
        value.ToString(format, CultureInfo.InvariantCulture);

    public static string Greekify(double value, string format = "0.#") =>
        value.ToString(format, Greek);

    public class DayPoint
    {
        public DateTime Date { get; set; }
        public string Label { get; set; } = string.Empty;
        public string ShortDate { get; set; } = string.Empty;
        public int? Peak { get; set; }
        public DateTime? PeakTime { get; set; }
        public int? Total { get; set; }
        public double? Average { get; set; }
        public int Samples { get; set; }
        public bool IsPeak { get; set; }
    }

    public class WeekChart
    {
        public bool HasData { get; set; }
        public List<DayPoint> Days { get; set; } = new();
        public DayPoint? Best { get; set; }
        public double AxisMax { get; set; } = 4;
        public double AxisStep { get; set; } = 1;
    }

    public class SpeedChart
    {
        public string? Error { get; set; }
        public List<SpeedTest> Tests { get; set; } = new();
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public double? AvgDownload { get; set; }
        public double? AvgUpload { get; set; }
        public double? AvgPing { get; set; }
        public SpeedTest? MinDownload { get; set; }
        public double AxisMax { get; set; } = 10;
        public double AxisStep { get; set; } = 5;
    }

    public async Task<IActionResult> OnPostCheckOnlineAsync()
    {
        var (online, offline) = await StatusChecker.CheckAllAsync(_context);

        StatusMessage = $"Ο έλεγχος ολοκληρώθηκε. Online: {online}, Offline: {offline}.";

        return RedirectToPage("/Index");
    }

    public int MacWarnings { get; set; }

    private async Task LoadDashboardAsync()
    {
        var computers = await _context.Computers.ToListAsync();

        PendingReports = await _context.FaultReports.CountAsync(r => r.Status == FaultReport.StatusNew);

        LowStock = (await _context.Consumables.ToListAsync())
            .Where(c => c.IsLow)
            .OrderBy(c => c.Quantity)
            .ToList();

        VeyonDownDevices = computers
            .Where(c => c.VeyonDown)
            .OrderBy(c => NetworkTools.IpToNumber(c.IpAddress))
            .ToList();

        DevicesByIp = computers
            .GroupBy(c => c.IpAddress.Trim())
            .ToDictionary(g => g.Key, g => g.First());

        Settings = await _context.AppSettings.FirstOrDefaultAsync(x => x.Id == 1)
            ?? new AppSettings
            {
                Id = 1,
                SchoolTitle = "School Network Manager",
                SchoolSubtitle = "Γρήγορη εικόνα του σχολικού δικτύου"
            };

        // Οι dynamic διευθύνσεις (DHCP) δεν είναι συσκευές: μένουν έξω από τα σύνολα.
        DynamicAddresses = computers.Count(c => c.IsDynamic);
        DynamicInUse = computers.Count(c => c.IsDynamic && c.IsOnline == true);
        var devices = computers.Where(c => !c.IsDynamic).ToList();

        TotalComputers = devices.Count;
        OnlineComputers = devices.Count(c => c.IsOnline == true);
        OfflineComputers = devices.Count(c => c.IsOnline == false);
        UnknownStatusComputers = devices.Count(c => c.IsOnline == null);
        VeyonComputers = devices.Count(c => c.HasVeyon);
        LanMessengerComputers = devices.Count(c => c.HasLanMessenger);

        Locations = devices
            .GroupBy(c => string.IsNullOrWhiteSpace(c.Location) ? "Χωρίς χώρο" : c.Location.Trim())
            .Select(g => new LocationSummary
            {
                Location = g.Key,
                Count = g.Count(),
                Online = g.Count(c => c.IsOnline == true),
                Percentage = TotalComputers == 0 ? 0 : g.Count() * 100.0 / TotalComputers
            })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Location)
            .Take(8)
            .ToList();

        MacWarnings = computers.Count(c => c.HasMacMismatch);

        var realDevices = computers
    .Where(c => !c.IsEmptySlot)
    .ToList();

        MachineTypeStats = realDevices
            .GroupBy(c => string.IsNullOrWhiteSpace(c.MachineType) ? MachineTypes.Default : c.MachineType.Trim())
            .Select(g => new MachineTypeSummary
            {
                MachineType = g.Key,
                Count = g.Count(),
                Percentage = realDevices.Count == 0 ? 0 : g.Count() * 100.0 / realDevices.Count
            })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.MachineType)
            .ToList();

        LatestComputers = computers
            .OrderByDescending(c => ChangeBadge.LastChange(c))
            .Take(8)
            .ToList();
    }

    public class LocationSummary
    {
        public string Location { get; set; } = string.Empty;
        public int Count { get; set; }
        public int Online { get; set; }
        public double Percentage { get; set; }

        public string PercentageText => $"{Percentage:0.0}%";

        public string PercentageCss =>
            Percentage.ToString("0.##", CultureInfo.InvariantCulture) + "%";

        public string OnlineCss =>
            (Count == 0 ? 0 : Online * 100.0 / Count).ToString("0.##", CultureInfo.InvariantCulture) + "%";
    }

    public class MachineTypeSummary
    {
        public string MachineType { get; set; } = string.Empty;
        public int Count { get; set; }
        public double Percentage { get; set; }

        public string PercentageText => $"{Percentage:0.0}%";

        public string PercentageCss =>
            Percentage.ToString("0.##", CultureInfo.InvariantCulture) + "%";
    }
}
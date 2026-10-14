using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;
using SchoolNetworkManager.Services;
using LocationsPage = SchoolNetworkManager.Pages.Locations.IndexModel;

namespace SchoolNetworkManager.Pages.Report;

/// <summary>Ετήσια αναφορά για τη Διεύθυνση (σχολικό έτος 1/9 – 31/8), έτοιμη για εκτύπωση / PDF.</summary>
public class IndexModel : PageModel
{
    private readonly AppDbContext _context;
    private readonly SettingsService _settings;

    public IndexModel(AppDbContext context, SettingsService settings)
    {
        _context = context;
        _settings = settings;
    }

    /// <summary>Έτος έναρξης σχολικού έτους (π.χ. 2026 → 2026-27).</summary>
    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)]
    public int? Year { get; set; }

    /// <summary>Κενό = όλο το σχολείο· αλλιώς μόνο ένας χώρος (π.χ. για τον τομεάρχη).</summary>
    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)]
    public string? Location { get; set; }

    public bool IsLocationReport => !string.IsNullOrWhiteSpace(Location);
    public string LocationLabel => Location == LocationsPage.NoLocationKey ? LocationsPage.NoLocationName : Location ?? string.Empty;
    public List<string> AllLocations { get; set; } = new();

    public AppSettings Settings => _settings.Current;
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public string SchoolYearLabel => $"{From.Year}-{(From.Year + 1) % 100:00}";
    public List<int> AvailableYears { get; set; } = new();

    public List<Computer> Devices { get; set; } = new();
    public List<string> Types { get; set; } = new();
    public List<LocationRow> ByLocation { get; set; } = new();

    public List<FaultRow> Faults { get; set; } = new();
    public int FaultsOpened { get; set; }
    public int FaultsClosed { get; set; }
    public int FaultsStillOpen { get; set; }
    public double? AvgRepairDays { get; set; }

    public List<AttentionRow> Attention { get; set; } = new();

    public List<Consumable> LowStock { get; set; } = new();
    public List<(string Name, int Used)> ConsumablesUsed { get; set; } = new();

    public int? PeakOnline { get; set; }
    public DateTime? PeakOnlineAt { get; set; }
    public double? AvgDailyPeak { get; set; }
    public DateTime? UsageDataFrom { get; set; }
    public int UsageDays { get; set; }
    public int? DevicesUsed { get; set; }
    public List<(int Hour, double Avg)> ByHourOfDay { get; set; } = new();

    public async Task OnGetAsync()
    {
        var now = DateTime.Now;
        var currentStart = now.Month >= 9 ? now.Year : now.Year - 1;
        var start = Math.Clamp(Year ?? currentStart, 2000, currentStart + 1);

        From = new DateTime(start, 9, 1);
        To = From.AddYears(1).AddTicks(-1);
        AvailableYears = Enumerable.Range(currentStart - 4, 5).Reverse().ToList();

        var everything = (await _context.Computers.AsNoTracking().ToListAsync())
            .Where(c => !c.IsEmptySlot)
            .ToList();

        AllLocations = everything
            .Select(c => string.IsNullOrWhiteSpace(c.Location) ? LocationsPage.NoLocationKey : c.Location.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k == LocationsPage.NoLocationKey)
            .ThenBy(k => k)
            .ToList();

        Devices = IsLocationReport
            ? everything.Where(c => LocationsPage.MatchesLocation(c, Location!.Trim())).OrderBy(c => NetworkTools.IpToNumber(c.IpAddress)).ToList()
            : everything;

        var byId = Devices.ToDictionary(d => d.Id);

        // ---------- Εξοπλισμός ----------
        Types = Devices
            .GroupBy(d => d.DisplayMachineType)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .ToList();

        ByLocation = Devices
            .GroupBy(d => string.IsNullOrWhiteSpace(d.Location) ? "Χωρίς χώρο" : d.Location.Trim())
            .OrderBy(g => g.Key == "Χωρίς χώρο")
            .ThenBy(g => g.Key)
            .Select(g => new LocationRow
            {
                Name = g.Key,
                Total = g.Count(),
                ByType = g.GroupBy(d => d.DisplayMachineType).ToDictionary(t => t.Key, t => t.Count())
            })
            .ToList();

        // ---------- Βλάβες ----------
        var faults = (await _context.Faults
                .Where(f => (f.CreatedAt >= From && f.CreatedAt <= To) || f.ResolvedAt == null)
                .OrderBy(f => f.CreatedAt)
                .ToListAsync())
            .Where(f => !IsLocationReport || byId.ContainsKey(f.ComputerId))
            .ToList();

        Faults = faults
            .Select(f => new FaultRow { Fault = f, Device = byId.GetValueOrDefault(f.ComputerId) })
            .ToList();

        var opened = faults.Where(f => f.CreatedAt >= From && f.CreatedAt <= To).ToList();
        FaultsOpened = opened.Count;
        FaultsClosed = faults.Count(f => f.ResolvedAt >= From && f.ResolvedAt <= To);
        FaultsStillOpen = faults.Count(f => f.ResolvedAt == null);

        var repaired = opened.Where(f => f.ResolvedAt != null).ToList();
        AvgRepairDays = repaired.Count == 0 ? null : repaired.Average(f => (f.ResolvedAt!.Value - f.CreatedAt).TotalDays);

        // ---------- Συσκευές που θέλουν προσοχή ----------
        var faultCounts = opened.GroupBy(f => f.ComputerId).ToDictionary(g => g.Key, g => g.Count());
        var openDown = faults.Where(f => f.ResolvedAt == null && f.Severity == Fault.SeverityDown).Select(f => f.ComputerId).ToHashSet();

        var last30 = now.AddDays(-30);
        var hours = await _context.DeviceHours
            .Where(h => h.Hour >= last30)
            .GroupBy(h => h.ComputerId)
            .Select(g => new { ComputerId = g.Key, Checks = g.Sum(x => x.Checks), Online = g.Sum(x => x.OnlineChecks) })
            .ToListAsync();
        var neverOn = hours.Where(h => h.Checks > 50 && h.Online == 0).Select(h => h.ComputerId).ToHashSet();

        foreach (var device in Devices.OrderBy(d => d.Location).ThenBy(d => NetworkTools.IpToNumber(d.IpAddress)))
        {
            var reasons = new List<string>();

            if (openDown.Contains(device.Id)) reasons.Add("⛔ εκτός λειτουργίας");
            if (faultCounts.TryGetValue(device.Id, out var count) && count >= 3) reasons.Add($"🔧 {count} βλάβες στη χρονιά");
            if (neverOn.Contains(device.Id)) reasons.Add("😴 δεν άνοιξε τις τελευταίες 30 ημέρες");

            var age = PurchaseYear(device);
            if (age != null && now.Year - age.Value >= 8) reasons.Add($"📅 παλαιότητα ({age} — {now.Year - age.Value} έτη)");

            var ram = device.SpecValues.GetValueOrDefault("pc_ram");
            if (ram != null && Regex.Match(ram, @"(\d+)\s*GB", RegexOptions.IgnoreCase) is { Success: true } m &&
                int.TryParse(m.Groups[1].Value, out var gb) && gb < 8 &&
                (device.MachineType == "Υπολογιστής" || device.MachineType == "Laptop"))
            {
                reasons.Add($"💾 λίγη μνήμη ({gb} GB)");
            }

            if (reasons.Count > 0)
            {
                Attention.Add(new AttentionRow { Device = device, Reasons = reasons });
            }
        }

        // ---------- Αναλώσιμα ----------
        var consumables = (await _context.Consumables.ToListAsync())
            .Where(c => !IsLocationReport ||
                        (c.StoredAt ?? string.Empty).Contains(LocationLabel, StringComparison.OrdinalIgnoreCase) ||
                        (c.CompatibleWith ?? string.Empty).Contains(LocationLabel, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var consumableIds = consumables.Select(c => c.Id).ToHashSet();
        LowStock = consumables.Where(c => c.IsLow).OrderBy(c => c.Quantity).ToList();

        var logs = (await _context.ConsumableLogs
                .Where(l => l.At >= From && l.At <= To && l.Change < 0)
                .ToListAsync())
            .Where(l => consumableIds.Contains(l.ConsumableId))
            .ToList();

        var names = consumables.ToDictionary(c => c.Id, c => c.Name);

        ConsumablesUsed = logs
            .GroupBy(l => l.ConsumableId)
            .Select(g => (names.GetValueOrDefault(g.Key, "(διαγραμμένο)"), -g.Sum(l => l.Change)))
            .OrderByDescending(x => x.Item2)
            .ToList();

        // ---------- Χρήση ----------
        if (IsLocationReport)
        {
            // Για έναν χώρο: από το ωριαίο ιστορικό των συσκευών του (κρατιέται 35 ημέρες).
            var ids = byId.Keys.ToList();
            var buckets = await _context.DeviceHours
                .Where(h => h.Hour >= From && h.Hour <= To && h.OnlineChecks > 0 && ids.Contains(h.ComputerId))
                .GroupBy(h => h.Hour)
                .Select(g => new { Hour = g.Key, Online = g.Count() })
                .ToListAsync();

            if (buckets.Count > 0)
            {
                var peak = buckets.OrderByDescending(b => b.Online).ThenBy(b => b.Hour).First();
                PeakOnline = peak.Online;
                PeakOnlineAt = peak.Hour;
                UsageDataFrom = buckets.Min(b => b.Hour);
                UsageDays = buckets.Select(b => b.Hour.Date).Distinct().Count();
                AvgDailyPeak = buckets
                    .Where(b => b.Hour.DayOfWeek != DayOfWeek.Saturday && b.Hour.DayOfWeek != DayOfWeek.Sunday)
                    .GroupBy(b => b.Hour.Date)
                    .Select(g => (double)g.Max(b => b.Online))
                    .DefaultIfEmpty()
                    .Average();

                // Ποιες ώρες της ημέρας δουλεύει το εργαστήριο (μέσος όρος ανοιχτών ανά ώρα, εργάσιμες).
                ByHourOfDay = buckets
                    .Where(b => b.Hour.DayOfWeek != DayOfWeek.Saturday && b.Hour.DayOfWeek != DayOfWeek.Sunday)
                    .GroupBy(b => b.Hour.Hour)
                    .Where(g => g.Key >= 7 && g.Key <= 20)
                    .OrderBy(g => g.Key)
                    .Select(g => (g.Key, g.Average(b => (double)b.Online)))
                    .ToList();
            }

            var usedIds = await _context.DeviceHours
                .Where(h => h.Hour >= From && h.Hour <= To && h.OnlineChecks > 0 && ids.Contains(h.ComputerId))
                .Select(h => h.ComputerId)
                .Distinct()
                .ToListAsync();
            DevicesUsed = usedIds.Count;
        }
        else
        {
            var snapshots = await _context.StatusSnapshots
                .Where(s => s.Timestamp >= From && s.Timestamp <= To)
                .ToListAsync();

            if (snapshots.Count > 0)
            {
                var peak = snapshots.OrderByDescending(s => s.Online).First();
                PeakOnline = peak.Online;
                PeakOnlineAt = peak.Timestamp;
                UsageDataFrom = snapshots.Min(s => s.Timestamp);
                UsageDays = snapshots.Select(x => x.Timestamp.Date).Distinct().Count();
                AvgDailyPeak = snapshots
                    .Where(s => s.Timestamp.DayOfWeek != DayOfWeek.Saturday && s.Timestamp.DayOfWeek != DayOfWeek.Sunday)
                    .GroupBy(s => s.Timestamp.Date)
                    .Select(g => (double)g.Max(s => s.Online))
                    .DefaultIfEmpty()
                    .Average();
            }
        }
    }

    private static int? PurchaseYear(Computer device)
    {
        var text = device.SpecValues.GetValueOrDefault("common_purchase");

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = Regex.Match(text, @"\b(19[89]\d|20[0-4]\d)\b");

        return match.Success ? int.Parse(match.Value) : null;
    }

    public class LocationRow
    {
        public string Name { get; set; } = string.Empty;
        public int Total { get; set; }
        public Dictionary<string, int> ByType { get; set; } = new();
    }

    public class FaultRow
    {
        public Fault Fault { get; set; } = new();
        public Computer? Device { get; set; }
        public string DeviceLabel => Device == null ? "(διαγραμμένη)" : string.IsNullOrWhiteSpace(Device.Name) ? Device.IpAddress : Device.Name;
        public string LocationLabel => string.IsNullOrWhiteSpace(Device?.Location) ? "—" : Device!.Location!;
    }

    public class AttentionRow
    {
        public Computer Device { get; set; } = new();
        public List<string> Reasons { get; set; } = new();
    }
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;
using SchoolNetworkManager.Services;

namespace SchoolNetworkManager.Pages.Network;

public class ScanModel : PageModel
{
    private readonly AppDbContext _context;
    private readonly DeviceProbe _probe;
    private readonly SettingsService _settings;

    public ScanModel(AppDbContext context, DeviceProbe probe, SettingsService settings)
    {
        _context = context;
        _probe = probe;
        _settings = settings;
    }

    [BindProperty]
    public string? Range { get; set; }

    [BindProperty]
    public bool IncludePorts { get; set; }

    [BindProperty]
    public string? NewLocation { get; set; }

    [BindProperty]
    public List<ScanRow> Rows { get; set; } = new();

    public bool HasScanned { get; set; }

    public int RangeSize { get; set; }

    public int MissingRegistered { get; set; }

    public List<string> AllLocations { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync()
    {
        Range = _settings.Current.ScanRange;
        await LoadLocationsAsync();
    }

    public async Task<IActionResult> OnPostScanAsync()
    {
        await LoadLocationsAsync();

        var ips = NetworkTools.ParseRange(Range);
        RangeSize = ips.Count;

        if (ips.Count == 0)
        {
            ModelState.AddModelError(nameof(Range), "Μη έγκυρο εύρος. Παράδειγμα: 192.168.2.1-254 ή 192.168.2.0/24");
            return Page();
        }

        var results = await _probe.ScanAsync(ips, IncludePorts, onlyAlive: true);
        var scannedSet = new HashSet<string>(ips);
        var computers = await _context.Computers.ToListAsync();
        var byIp = computers
            .GroupBy(c => c.IpAddress.Trim())
            .ToDictionary(g => g.Key, g => g.First());

        var now = DateTime.Now;
        var foundIps = new HashSet<string>(results.Select(r => r.Ip));

        Rows = new List<ScanRow>();

        foreach (var result in results)
        {
            byIp.TryGetValue(result.Ip, out var existing);

            var row = new ScanRow
            {
                Ip = result.Ip,
                Mac = result.Mac,
                Name = result.Name,
                Vendor = result.Vendor,
                OpenPorts = result.PortsChecked ? result.OpenPortsText : null,
                MachineType = result.SuggestedType ?? "Άγνωστο",
                HasVeyon = result.HasVeyon,
                ExistingId = existing?.Id,
                ExistingName = existing?.Name,
                ExistingLocation = existing?.Location,
                Selected = existing == null
            };

            if (existing != null)
            {
                // Ενημέρωση της υπάρχουσας καταχώρησης με ό,τι βρέθηκε
                existing.IsOnline = true;
                existing.LastCheckedAt = now;
                existing.LastSeenOnline = now;

                if (string.IsNullOrWhiteSpace(existing.Name) && !string.IsNullOrWhiteSpace(result.Name))
                {
                    existing.Name = result.Name;
                    existing.UpdatedAt = now;
                }

                if (!string.IsNullOrWhiteSpace(result.Mac))
                {
                    if (string.IsNullOrWhiteSpace(existing.MacAddress))
                    {
                        existing.MacAddress = result.Mac;
                        existing.UpdatedAt = now;
                    }

                    existing.DetectedMac = result.Mac;
                }

                if (!string.IsNullOrWhiteSpace(result.Vendor))
                {
                    existing.Vendor = result.Vendor;
                }

                if (result.PortsChecked)
                {
                    existing.OpenPorts = result.OpenPortsText;
                }

                row.MacChanged = existing.HasMacMismatch;
                row.RegisteredMac = existing.MacAddress;
            }

            Rows.Add(row);
        }

        // Καταχωρημένες συσκευές μέσα στο εύρος που δεν απάντησαν
        foreach (var computer in computers)
        {
            var ip = computer.IpAddress.Trim();

            if (scannedSet.Contains(ip) && !foundIps.Contains(ip) && !computer.IsEmptySlot)
            {
                computer.IsOnline = false;
                computer.LastCheckedAt = now;
                MissingRegistered++;
            }
        }

        await _context.SaveChangesAsync();

        HasScanned = true;
        ModelState.Clear();

        return Page();
    }

    public async Task<IActionResult> OnPostAddAsync()
    {
        var selected = Rows.Where(r => r.Selected && r.ExistingId == null).ToList();

        if (selected.Count == 0)
        {
            StatusMessage = "Δεν επιλέχθηκε καμία νέα συσκευή.";
            return RedirectToPage();
        }

        var existingIps = new HashSet<string>(
            await _context.Computers.Select(c => c.IpAddress.Trim()).ToListAsync());

        var location = string.IsNullOrWhiteSpace(NewLocation) ? null : NewLocation.Trim();
        var now = DateTime.Now;
        var added = 0;

        foreach (var row in selected)
        {
            var ip = (row.Ip ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(ip) || existingIps.Contains(ip))
            {
                continue;
            }

            _context.Computers.Add(new Computer
            {
                IpAddress = ip,
                Name = string.IsNullOrWhiteSpace(row.Name) ? null : row.Name.Trim(),
                MacAddress = NetworkTools.NormalizeMac(row.Mac),
                DetectedMac = NetworkTools.NormalizeMac(row.Mac),
                Vendor = row.Vendor,
                OpenPorts = row.OpenPorts,
                MachineType = string.IsNullOrWhiteSpace(row.MachineType) ? MachineTypes.Default : row.MachineType,
                Location = location,
                HasVeyon = row.HasVeyon,
                IsOnline = true,
                LastCheckedAt = now,
                LastSeenOnline = now,
                CreatedAt = now
            });

            existingIps.Add(ip);
            added++;
        }

        await _context.SaveChangesAsync();

        StatusMessage = added == 1
            ? "Προστέθηκε 1 νέα συσκευή."
            : $"Προστέθηκαν {added} νέες συσκευές.";

        return RedirectToPage("/Computers/Index", new { LocationFilter = location });
    }

    private async Task LoadLocationsAsync()
    {
        var locations = await _context.Computers
            .Where(c => c.Location != null && c.Location != "")
            .Select(c => c.Location!)
            .ToListAsync();

        AllLocations = locations
            .Select(l => l.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(l => l)
            .ToList();
    }

    public class ScanRow
    {
        public bool Selected { get; set; }
        public string? Ip { get; set; }
        public string? Mac { get; set; }
        public string? Name { get; set; }
        public string? Vendor { get; set; }
        public string? OpenPorts { get; set; }
        public string? MachineType { get; set; }
        public bool HasVeyon { get; set; }

        public int? ExistingId { get; set; }
        public string? ExistingName { get; set; }
        public string? ExistingLocation { get; set; }
        public bool MacChanged { get; set; }
        public string? RegisteredMac { get; set; }
    }
}

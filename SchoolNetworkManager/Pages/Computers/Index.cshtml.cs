using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;
using SchoolNetworkManager.Services;

namespace SchoolNetworkManager.Pages.Computers;

public class IndexModel : PageModel
{
    private readonly AppDbContext _context;
    private readonly DeviceProbe _probe;
    private readonly SettingsService _settings;

    public IndexModel(AppDbContext context, DeviceProbe probe, SettingsService settings)
    {
        _context = context;
        _probe = probe;
        _settings = settings;
    }

    public List<Computer> Computers { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public string? VeyonFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? LanFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? StatusFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public string SortBy { get; set; } = "ip";

    [BindProperty(SupportsGet = true)]
    public string? LocationFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? TypeFilter { get; set; }

    /// <summary>new = μόνο νέες, changed = νέες ή ενημερωμένες.</summary>
    [BindProperty(SupportsGet = true)]
    public string? ChangeFilter { get; set; }

    public int ChangedCount { get; set; }

    /// <summary>static = μόνο σταθερές συσκευές, dynamic = μόνο διευθύνσεις DHCP.</summary>
    [BindProperty(SupportsGet = true)]
    public string? AddressFilter { get; set; }

    public int AddressTotal { get; set; }
    public int StaticCount { get; set; }
    public int DynamicCount { get; set; }
    public int CleanupCandidates { get; set; }

    /// <summary>Συσκευές με ανοιχτή βλάβη (true = εκτός λειτουργίας).</summary>
    public Dictionary<int, bool> OpenFaults { get; set; } = new();

    public List<string> AllLocations { get; set; } = new();

    public List<string> AllTypes { get; set; } = new();

    public int PageSize { get; set; } = 25;

    public int TotalCount { get; set; }

    public int TotalPages { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? StatusKind { get; set; }

    public async Task OnGetAsync()
    {
        PageSize = Math.Clamp(_settings.Current.PageSize, 5, 500);

        var all = await _context.Computers
            .Select(c => new { c.Location, c.MachineType })
            .ToListAsync();

        var everything = await _context.Computers.AsNoTracking().ToListAsync();
        var touched = await AddressCleanup.TouchedAsync(_context);

        AddressTotal = everything.Count;
        DynamicCount = everything.Count(c => c.IsDynamic);
        StaticCount = AddressTotal - DynamicCount;
        CleanupCandidates = everything.Count(c => AddressCleanup.Judge(c, touched) != AddressCleanup.Verdict.Keep);

        AllLocations = all
            .Select(c => c.Location?.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(l => l)
            .ToList();

        AllTypes = all
            .Select(c => string.IsNullOrWhiteSpace(c.MachineType) ? MachineTypes.Default : c.MachineType.Trim())
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        var since = ChangeBadge.Since(_settings.Current);
        ChangedCount = _settings.Current.NewBadgeDays <= 0
            ? 0
            : await _context.Computers.CountAsync(c => c.CreatedAt > since || (c.UpdatedAt != null && c.UpdatedAt > since));

        OpenFaults = await FaultLookup.OpenByComputerAsync(_context);

        await LoadComputersAsync();
    }

    /// <summary>«Όλα ειδωμένα»: σβήνει τις ενδείξεις ΝΕΟ / ΕΝΗΜ. για όλους.</summary>
    public async Task<IActionResult> OnPostClearBadgesAsync()
    {
        var settings = await _context.AppSettings.FirstOrDefaultAsync(x => x.Id == 1);

        if (settings != null)
        {
            settings.BadgesClearedAt = DateTime.Now;
            await _context.SaveChangesAsync();
            _settings.Update(settings);
        }

        StatusKind = "success";
        StatusMessage = "Οι ενδείξεις ΝΕΟ / ΕΝΗΜ. καθαρίστηκαν.";
        ChangeFilter = null;

        return RedirectToPage("./Index", CurrentRouteValues());
    }

    public object CurrentRouteValues() => new
    {
        SearchTerm,
        PageNumber,
        VeyonFilter,
        LanFilter,
        StatusFilter,
        SortBy,
        LocationFilter,
        TypeFilter,
        ChangeFilter,
        AddressFilter
    };

    public async Task<IActionResult> OnPostDetectAsync()
    {
        var computers = (await GetFilteredComputersAsync(applySorting: false))
            .Where(c => !c.IsEmptySlot)
            .ToList();

        if (computers.Count > 300)
        {
            StatusKind = "warning";
            StatusMessage = "Πάρα πολλές συσκευές για μία ανίχνευση. Περιόρισε τη λίστα με φίλτρο (π.χ. χώρο).";
            return RedirectToPage("./Index", CurrentRouteValues());
        }

        var results = await _probe.ScanAsync(computers.Select(c => c.IpAddress), includePorts: true, onlyAlive: false);
        var byIp = results.ToDictionary(r => r.Ip, StringComparer.OrdinalIgnoreCase);
        var now = DateTime.Now;
        var online = 0;
        var filled = 0;

        foreach (var computer in computers)
        {
            if (!byIp.TryGetValue(computer.IpAddress.Trim(), out var result))
            {
                continue;
            }

            computer.IsOnline = result.Online;
            computer.LastCheckedAt = now;

            if (!result.Online)
            {
                continue;
            }

            online++;
            computer.LastSeenOnline = now;
            computer.OpenPorts = result.OpenPortsText;

            if (!string.IsNullOrWhiteSpace(result.Vendor))
            {
                computer.Vendor = result.Vendor;
            }

            if (!string.IsNullOrWhiteSpace(result.Name) && string.IsNullOrWhiteSpace(computer.Name))
            {
                computer.Name = result.Name;
                filled++;
                computer.UpdatedAt = now;
            }

            if (!string.IsNullOrWhiteSpace(result.Mac))
            {
                if (string.IsNullOrWhiteSpace(computer.MacAddress))
                {
                    computer.MacAddress = result.Mac;
                    filled++;
                    computer.UpdatedAt = now;
                }

                computer.DetectedMac = result.Mac;
            }

            if (result.HasVeyon && !computer.HasVeyon)
            {
                computer.HasVeyon = true;
                filled++;
                computer.UpdatedAt = now;
            }

            if (result.SuggestedType != null && computer.MachineType == "Άγνωστο")
            {
                computer.MachineType = result.SuggestedType;
                filled++;
                computer.UpdatedAt = now;
            }
        }

        await _context.SaveChangesAsync();

        StatusKind = "success";
        StatusMessage = $"Η ανίχνευση ολοκληρώθηκε σε {computers.Count} συσκευές. " +
                        $"Online: {online}. Συμπληρώθηκαν {filled} κενά στοιχεία.";

        return RedirectToPage("./Index", CurrentRouteValues());
    }

    public async Task<IActionResult> OnPostWakeAsync(int? id)
    {
        var computers = id.HasValue
            ? await _context.Computers.Where(c => c.Id == id.Value).ToListAsync()
            : await GetFilteredComputersAsync(applySorting: false);

        var withMac = computers
            .Where(c => !c.IsEmptySlot && !string.IsNullOrWhiteSpace(c.MacAddress))
            .ToList();

        var sent = 0;

        foreach (var computer in withMac)
        {
            if (await NetworkTools.SendWakeOnLanAsync(computer.MacAddress, _settings.Current.WolBroadcast))
            {
                sent++;
            }
        }

        var missing = computers.Count(c => !c.IsEmptySlot) - withMac.Count;

        StatusKind = sent > 0 ? "success" : "warning";
        StatusMessage = sent > 0
            ? $"Στάλθηκε σήμα αφύπνισης σε {sent} συσκευές." +
              (missing > 0 ? $" ({missing} χωρίς MAC δεν μπορούν να ανάψουν.)" : string.Empty)
            : "Δεν στάλθηκε σήμα αφύπνισης — οι συσκευές δεν έχουν MAC Address.";

        return RedirectToPage("./Index", CurrentRouteValues());
    }

    public async Task<IActionResult> OnPostCheckOnlineAsync()
    {
        var computers = await GetFilteredComputersAsync(applySorting: false);
        var (online, offline) = await StatusChecker.CheckAsync(_context, computers);

        await _context.SaveChangesAsync();

        StatusKind = "success";
        StatusMessage = $"Ο έλεγχος ολοκληρώθηκε. Online: {online}, Offline: {offline}.";

        return RedirectToPage("./Index", CurrentRouteValues());
    }

    public async Task<IActionResult> OnGetExportAsync()
    {
        var computers = await GetFilteredComputersAsync();

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("School Network");

        worksheet.Cell(1, 1).Value = "ID";
        worksheet.Cell(1, 2).Value = "IP Address";
        worksheet.Cell(1, 3).Value = "Machine Type";
        worksheet.Cell(1, 4).Value = "Name";
        worksheet.Cell(1, 5).Value = "MAC Address";
        worksheet.Cell(1, 6).Value = "Location";
        worksheet.Cell(1, 7).Value = "Username";
        worksheet.Cell(1, 8).Value = "Password";
        worksheet.Cell(1, 9).Value = "Extras";
        worksheet.Cell(1, 10).Value = "LAN Messenger";
        worksheet.Cell(1, 11).Value = "Veyon";
        worksheet.Cell(1, 12).Value = "Σημειώσεις";
        worksheet.Cell(1, 13).Value = "Χαρακτηριστικά";
        worksheet.Cell(1, 14).Value = "Status";
        worksheet.Cell(1, 15).Value = "Last Checked";
        worksheet.Cell(1, 16).Value = "Created";
        worksheet.Cell(1, 17).Value = "Updated";
        worksheet.Cell(1, 18).Value = "Κατασκευαστής";
        worksheet.Cell(1, 19).Value = "Ανοιχτά ports";
        worksheet.Cell(1, 20).Value = "Τελευταία φορά online";
        worksheet.Cell(1, 21).Value = "Διεύθυνση";

        var headerRange = worksheet.Range(1, 1, 1, 21);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;

        var row = 2;

        foreach (var computer in computers)
        {
            worksheet.Cell(row, 1).Value = computer.Id;
            worksheet.Cell(row, 2).Value = computer.IpAddress;
            worksheet.Cell(row, 3).Value = computer.DisplayMachineType;
            worksheet.Cell(row, 4).Value = computer.Name;
            worksheet.Cell(row, 5).Value = computer.MacAddress;
            worksheet.Cell(row, 6).Value = computer.Location;
            worksheet.Cell(row, 7).Value = computer.Username;
            worksheet.Cell(row, 8).Value = computer.Password;
            worksheet.Cell(row, 9).Value = computer.Extras;
            worksheet.Cell(row, 10).Value = computer.HasLanMessenger ? "Ναι" : "Όχι";
            worksheet.Cell(row, 11).Value = computer.HasVeyon ? "Ναι" : "Όχι";
            worksheet.Cell(row, 12).Value = computer.Notes;
            worksheet.Cell(row, 13).Value = SpecsAsText(computer);
            worksheet.Cell(row, 14).Value = computer.IsOnline == true ? "Online" :
                                             computer.IsOnline == false ? "Offline" :
                                             "Άγνωστο";
            worksheet.Cell(row, 15).Value = computer.LastCheckedAt?.ToString("dd/MM/yyyy HH:mm");
            worksheet.Cell(row, 16).Value = computer.CreatedAt.ToString("dd/MM/yyyy HH:mm");
            worksheet.Cell(row, 17).Value = computer.UpdatedAt?.ToString("dd/MM/yyyy HH:mm");
            worksheet.Cell(row, 18).Value = computer.Vendor;
            worksheet.Cell(row, 19).Value = computer.OpenPorts;
            worksheet.Cell(row, 20).Value = computer.LastSeenOnline?.ToString("dd/MM/yyyy HH:mm");
            worksheet.Cell(row, 21).Value = computer.IsDynamic ? "Dynamic" : "Static";

            row++;
        }

        worksheet.Columns().AdjustToContents();
        worksheet.SheetView.FreezeRows(1);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var fileName = $"SchoolNetworkManager_{DateTime.Now:yyyy-MM-dd_HH-mm}.xlsx";

        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName
        );
    }

    private async Task LoadComputersAsync()
    {
        if (PageNumber < 1)
        {
            PageNumber = 1;
        }

        var allComputers = await GetFilteredComputersAsync();

        TotalCount = allComputers.Count;
        TotalPages = (int)Math.Ceiling(TotalCount / (double)PageSize);

        if (TotalPages == 0)
        {
            TotalPages = 1;
        }

        if (PageNumber > TotalPages)
        {
            PageNumber = TotalPages;
        }

        Computers = allComputers
            .Skip((PageNumber - 1) * PageSize)
            .Take(PageSize)
            .ToList();
    }

    private async Task<List<Computer>> GetFilteredComputersAsync(bool applySorting = true)
    {
        var query = _context.Computers.AsQueryable();

        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            var term = SearchTerm.Trim();

            query = query.Where(c =>
                c.IpAddress.Contains(term) ||
                c.MachineType.Contains(term) ||
                (c.Name != null && c.Name.Contains(term)) ||
                (c.MacAddress != null && c.MacAddress.Contains(term)) ||
                (c.Location != null && c.Location.Contains(term)) ||
                (c.Username != null && c.Username.Contains(term)) ||
                (c.Extras != null && c.Extras.Contains(term)) ||
                (c.Notes != null && c.Notes.Contains(term)) ||
                (c.Comments != null && c.Comments.Contains(term)) ||
                (c.Specs != null && c.Specs.Contains(term))
            );
        }

        if (!string.IsNullOrWhiteSpace(LocationFilter))
        {
            if (LocationFilter == "__none__")
            {
                query = query.Where(c => c.Location == null || c.Location.Trim() == "");
            }
            else
            {
                var location = LocationFilter.Trim();
                query = query.Where(c => c.Location != null && c.Location.Trim() == location);
            }
        }

        if (AddressFilter == "static")
        {
            query = query.Where(c => !c.IsDynamic);
        }
        else if (AddressFilter == "dynamic")
        {
            query = query.Where(c => c.IsDynamic);
        }

        if (!string.IsNullOrWhiteSpace(TypeFilter))
        {
            var type = TypeFilter.Trim();
            query = query.Where(c => c.MachineType == type);
        }

        if (ChangeFilter == "new" || ChangeFilter == "changed")
        {
            var since = ChangeBadge.Since(_settings.Current);

            query = ChangeFilter == "new"
                ? query.Where(c => c.CreatedAt > since)
                : query.Where(c => c.CreatedAt > since || (c.UpdatedAt != null && c.UpdatedAt > since));
        }

        if (VeyonFilter == "yes")
        {
            query = query.Where(c => c.HasVeyon);
        }
        else if (VeyonFilter == "no")
        {
            query = query.Where(c => !c.HasVeyon);
        }

        if (LanFilter == "yes")
        {
            query = query.Where(c => c.HasLanMessenger);
        }
        else if (LanFilter == "no")
        {
            query = query.Where(c => !c.HasLanMessenger);
        }

        if (StatusFilter == "online")
        {
            query = query.Where(c => c.IsOnline == true);
        }
        else if (StatusFilter == "offline")
        {
            query = query.Where(c => c.IsOnline == false);
        }
        else if (StatusFilter == "unknown")
        {
            query = query.Where(c => c.IsOnline == null);
        }

        var computers = await query.ToListAsync();

        if (!applySorting)
        {
            return computers;
        }

        return SortBy switch
        {
            "type" => computers
                .OrderBy(c => c.DisplayMachineType)
                .ThenBy(c => IpToNumber(c.IpAddress))
                .ToList(),

            "name" => computers
                .OrderBy(c => c.Name)
                .ThenBy(c => IpToNumber(c.IpAddress))
                .ToList(),

            "location" => computers
                .OrderBy(c => c.Location)
                .ThenBy(c => IpToNumber(c.IpAddress))
                .ToList(),

            "recent" => computers
                .OrderByDescending(c => ChangeBadge.LastChange(c))
                .ThenBy(c => IpToNumber(c.IpAddress))
                .ToList(),

            "veyon" => computers
                .OrderByDescending(c => c.HasVeyon)
                .ThenBy(c => IpToNumber(c.IpAddress))
                .ToList(),

            "lan" => computers
                .OrderByDescending(c => c.HasLanMessenger)
                .ThenBy(c => IpToNumber(c.IpAddress))
                .ToList(),

            "status" => computers
                .OrderByDescending(c => c.IsOnline == true)
                .ThenBy(c => c.IsOnline == null)
                .ThenBy(c => IpToNumber(c.IpAddress))
                .ToList(),

            _ => computers
                .OrderBy(c => IpToNumber(c.IpAddress))
                .ThenBy(c => c.Name)
                .ToList()
        };
    }

    private static long IpToNumber(string ip) => NetworkTools.IpToNumber(ip);

    private static string SpecsAsText(Computer computer)
    {
        var values = computer.SpecValues;

        if (values.Count == 0)
        {
            return string.Empty;
        }

        var parts = new List<string>();

        foreach (var group in DeviceSpecs.AllGroups)
        {
            foreach (var field in group.Fields)
            {
                if (values.TryGetValue(DeviceSpecs.FormKey(group, field), out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    parts.Add($"{field.Label}: {value}");
                }
            }
        }

        return string.Join(" | ", parts.Distinct());
    }

    public string GetLocationStyle(Computer computer)
    {
        var location = computer.Location?.Trim();

        if (string.IsNullOrWhiteSpace(location) || location.Equals("EMPTY", StringComparison.OrdinalIgnoreCase))
        {
            return "--location-hue: 220;";
        }

        var hash = 0;

        foreach (var ch in location)
        {
            hash = (hash * 31) + ch;
        }

        var hue = Math.Abs(hash) % 360;

        return $"--location-hue: {hue};";
    }
}
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;
using System.Net.NetworkInformation;

namespace SchoolNetworkManager.Pages.Computers;

public class IndexModel : PageModel
{
    private readonly AppDbContext _context;

    public IndexModel(AppDbContext context)
    {
        _context = context;
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

    public int PageSize { get; set; } = 25;

    public int TotalCount { get; set; }

    public int TotalPages { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync()
    {
        await LoadComputersAsync();
    }

    public async Task<IActionResult> OnPostCheckOnlineAsync()
    {
        var computers = await GetFilteredComputersAsync(applySorting: false);

        var semaphore = new SemaphoreSlim(25);

        var tasks = computers.Select(async computer =>
        {
            await semaphore.WaitAsync();

            try
            {
                var isOnline = await PingHostAsync(computer.IpAddress);

                return new
                {
                    computer.Id,
                    IsOnline = isOnline
                };
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(tasks);

        foreach (var result in results)
        {
            var computer = computers.First(c => c.Id == result.Id);
            computer.IsOnline = result.IsOnline;
            computer.LastCheckedAt = DateTime.Now;
        }

        await _context.SaveChangesAsync();

        var online = results.Count(x => x.IsOnline);
        var offline = results.Count(x => !x.IsOnline);

        StatusMessage = $"Ο έλεγχος ολοκληρώθηκε. Online: {online}, Offline: {offline}.";

        return RedirectToPage("./Index", new
        {
            SearchTerm,
            PageNumber,
            VeyonFilter,
            LanFilter,
            StatusFilter,
            SortBy
        });
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
        worksheet.Cell(1, 12).Value = "NOTES";
        worksheet.Cell(1, 13).Value = "Σημειώσεις";
        worksheet.Cell(1, 14).Value = "Status";
        worksheet.Cell(1, 15).Value = "Last Checked";
        worksheet.Cell(1, 16).Value = "Created";
        worksheet.Cell(1, 17).Value = "Updated";

        var headerRange = worksheet.Range(1, 1, 1, 17);
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
            worksheet.Cell(row, 13).Value = computer.Comments;
            worksheet.Cell(row, 14).Value = computer.IsOnline == true ? "Online" :
                                             computer.IsOnline == false ? "Offline" :
                                             "Άγνωστο";
            worksheet.Cell(row, 15).Value = computer.LastCheckedAt?.ToString("dd/MM/yyyy HH:mm");
            worksheet.Cell(row, 16).Value = computer.CreatedAt.ToString("dd/MM/yyyy HH:mm");
            worksheet.Cell(row, 17).Value = computer.UpdatedAt?.ToString("dd/MM/yyyy HH:mm");

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
                (c.Comments != null && c.Comments.Contains(term))
            );
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

    private static async Task<bool> PingHostAsync(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return false;
        }

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(ipAddress, 800);

            return reply.Status == IPStatus.Success;
        }
        catch
        {
            return false;
        }
    }

    private static long IpToNumber(string ip)
    {
        var parts = ip.Split('.');

        if (parts.Length != 4)
        {
            return long.MaxValue;
        }

        if (!byte.TryParse(parts[0], out var a) ||
            !byte.TryParse(parts[1], out var b) ||
            !byte.TryParse(parts[2], out var c) ||
            !byte.TryParse(parts[3], out var d))
        {
            return long.MaxValue;
        }

        return ((long)a << 24) + ((long)b << 16) + ((long)c << 8) + d;
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
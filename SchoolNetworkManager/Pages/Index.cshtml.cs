using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;
using System.Globalization;
using System.Net.NetworkInformation;

namespace SchoolNetworkManager.Pages;

public class IndexModel : PageModel
{
    private readonly AppDbContext _context;

    public IndexModel(AppDbContext context)
    {
        _context = context;
    }

    [TempData]
    public string? StatusMessage { get; set; }

    public int TotalComputers { get; set; }
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
    }

    public async Task<IActionResult> OnPostCheckOnlineAsync()
    {
        var computers = await _context.Computers.ToListAsync();

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

        return RedirectToPage("/Index");
    }

    private async Task LoadDashboardAsync()
    {
        var computers = await _context.Computers.ToListAsync();

        Settings = await _context.AppSettings.FirstOrDefaultAsync(x => x.Id == 1)
            ?? new AppSettings
            {
                Id = 1,
                SchoolTitle = "School Network Manager",
                SchoolSubtitle = "Γρήγορη εικόνα του σχολικού δικτύου"
            };

        TotalComputers = computers.Count;
        OnlineComputers = computers.Count(c => c.IsOnline == true);
        OfflineComputers = computers.Count(c => c.IsOnline == false);
        UnknownStatusComputers = computers.Count(c => c.IsOnline == null);
        VeyonComputers = computers.Count(c => c.HasVeyon);
        LanMessengerComputers = computers.Count(c => c.HasLanMessenger);

        Locations = computers
            .GroupBy(c => string.IsNullOrWhiteSpace(c.Location) ? "Χωρίς χώρο" : c.Location.Trim())
            .Select(g => new LocationSummary
            {
                Location = g.Key,
                Count = g.Count(),
                Percentage = TotalComputers == 0 ? 0 : g.Count() * 100.0 / TotalComputers
            })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Location)
            .Take(8)
            .ToList();

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
            .OrderByDescending(c => c.CreatedAt)
            .Take(8)
            .ToList();
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

    public class LocationSummary
    {
        public string Location { get; set; } = string.Empty;
        public int Count { get; set; }
        public double Percentage { get; set; }

        public string PercentageText => $"{Percentage:0.0}%";

        public string PercentageCss =>
            Percentage.ToString("0.##", CultureInfo.InvariantCulture) + "%";
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
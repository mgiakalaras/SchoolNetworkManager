using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;

namespace SchoolNetworkManager.Services;

/// <summary>
/// Έλεγχος online/offline για λίστα συσκευών. Ενημερώνει κατάσταση, «τελευταία φορά online»
/// και συμπληρώνει τη MAC αν λείπει (ή σημειώνει αν άλλαξε).
/// </summary>
public static class StatusChecker
{
    /// <summary>Έλεγχος + καταγραφή στο ιστορικό λειτουργίας (η αποθήκευση γίνεται από όποιον καλεί).</summary>
    public static async Task<(int Online, int Offline)> CheckAsync(AppDbContext db, IList<Computer> computers)
    {
        var previous = computers.ToDictionary(c => c.Id, c => c.IsOnline);
        var counts = await CheckAsync(computers);

        await RecordHistoryAsync(db, computers, previous);

        return counts;
    }

    private static async Task RecordHistoryAsync(AppDbContext db, IList<Computer> computers, Dictionary<int, bool?> previous)
    {
        var real = computers.Where(c => c.Id > 0 && !c.IsEmptySlot).ToList();

        if (real.Count == 0)
        {
            return;
        }

        var now = DateTime.Now;
        var hour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);
        var ids = real.Select(c => c.Id).ToList();

        var rows = await db.DeviceHours
            .Where(h => h.Hour == hour && ids.Contains(h.ComputerId))
            .ToDictionaryAsync(h => h.ComputerId);

        foreach (var computer in real)
        {
            if (!rows.TryGetValue(computer.Id, out var row))
            {
                row = new DeviceHour { ComputerId = computer.Id, Hour = hour };
                db.DeviceHours.Add(row);
                rows[computer.Id] = row;
            }

            row.Checks++;

            if (computer.IsOnline == true)
            {
                row.OnlineChecks++;
            }
            else if (previous.TryGetValue(computer.Id, out var before) && before == true)
            {
                row.Drops++;
            }
        }
    }

    public static async Task<(int Online, int Offline)> CheckAsync(IList<Computer> computers)
    {
        var semaphore = new SemaphoreSlim(32);

        var tasks = computers.Select(async computer =>
        {
            await semaphore.WaitAsync();

            try
            {
                var isOnline = await NetworkTools.PingAsync(computer.IpAddress);

                return new { Computer = computer, IsOnline = isOnline };
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(tasks);
        var arp = await NetworkTools.GetArpTableAsync();
        var now = DateTime.Now;

        foreach (var result in results)
        {
            var computer = result.Computer;

            computer.IsOnline = result.IsOnline;
            computer.LastCheckedAt = now;

            if (!result.IsOnline)
            {
                continue;
            }

            computer.LastSeenOnline = now;

            var ip = (computer.IpAddress ?? string.Empty).Trim();

            if (arp.TryGetValue(ip, out var mac))
            {
                if (string.IsNullOrWhiteSpace(computer.MacAddress))
                {
                    computer.MacAddress = mac;
                    computer.UpdatedAt = now;
                }

                computer.DetectedMac = mac;
            }
        }

        await CheckVeyonAsync(computers, now);

        return (results.Count(r => r.IsOnline), results.Count(r => !r.IsOnline));
    }

    public const int VeyonPort = 11100;

    /// <summary>
    /// Φύλακας Veyon: σε ανοιχτούς υπολογιστές με Veyon, ελέγχει ότι η υπηρεσία απαντά (θύρα 11100).
    /// Αν κάποιος την έκλεισε, ο υπολογιστής σημειώνεται «Veyon σταμάτησε».
    /// </summary>
    private static async Task CheckVeyonAsync(IList<Computer> computers, DateTime now)
    {
        var targets = computers.Where(c => c.HasVeyon && c.IsOnline == true && !c.IsEmptySlot).ToList();
        var semaphore = new SemaphoreSlim(32);

        var checks = targets.Select(async computer =>
        {
            await semaphore.WaitAsync();

            try
            {
                // Δύο προσπάθειες, για να μη βγαίνει ψεύτικος συναγερμός από μια στιγμιαία καθυστέρηση
                var open = await NetworkTools.IsPortOpenAsync(computer.IpAddress, VeyonPort, 800) ||
                           await NetworkTools.IsPortOpenAsync(computer.IpAddress, VeyonPort, 1500);

                return (Computer: computer, Open: open);
            }
            finally
            {
                semaphore.Release();
            }
        });

        foreach (var (computer, open) in await Task.WhenAll(checks))
        {
            if (open)
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

        // Κλειστοί υπολογιστές: δεν ξέρουμε — καθαρίζουμε την ένδειξη ώστε να μη μένει «κολλημένη».
        foreach (var computer in computers.Where(c => c.IsOnline != true || !c.HasVeyon))
        {
            computer.VeyonDown = false;
            computer.VeyonDownSince = null;
        }
    }

    public static async Task<(int Online, int Offline)> CheckAllAsync(AppDbContext db)
    {
        var computers = await db.Computers.ToListAsync();
        var counts = await CheckAsync(db, computers);

        // Καταγραφή για το γράφημα της εβδομάδας (χωρίς τις κενές θέσεις)
        var real = computers.Where(c => !c.IsEmptySlot).ToList();

        db.StatusSnapshots.Add(new StatusSnapshot
        {
            Timestamp = DateTime.Now,
            Online = real.Count(c => c.IsOnline == true),
            Total = real.Count
        });

        var cutoff = DateTime.Now.AddDays(-90);
        var old = await db.StatusSnapshots.Where(s => s.Timestamp < cutoff).ToListAsync();
        db.StatusSnapshots.RemoveRange(old);

        // Ιστορικό ανά συσκευή: κρατάμε 35 ημέρες
        var hoursCutoff = DateTime.Now.AddDays(-35);
        await db.DeviceHours.Where(h => h.Hour < hoursCutoff).ExecuteDeleteAsync();

        await db.SaveChangesAsync();

        return counts;
    }
}

/// <summary>
/// Τρέχει στο παρασκήνιο και ελέγχει όλες τις συσκευές ανά Χ λεπτά (ρύθμιση).
/// </summary>
public class MonitorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SettingsService _settings;
    private readonly ILogger<MonitorService> _logger;

    public MonitorService(
        IServiceScopeFactory scopeFactory,
        SettingsService settings,
        ILogger<MonitorService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var minutes = _settings.Current.MonitorIntervalMinutes;

            if (minutes > 0)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    await StatusChecker.CheckAllAsync(db);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Ο αυτόματος έλεγχος συσκευών απέτυχε.");
                }
            }

            var wait = TimeSpan.FromMinutes(minutes > 0 ? minutes : 1);

            try
            {
                await Task.Delay(wait, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}

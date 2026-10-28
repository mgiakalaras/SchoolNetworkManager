using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;

namespace SchoolNetworkManager.Services;

/// <summary>
/// Αυτόματο βραδινό κλείσιμο: αν ένας χώρος έχει ορισμένη ώρα (π.χ. «21:00»),
/// στέλνει εντολή κλεισίματος σε όσους υπολογιστές του είναι ακόμη ανοιχτοί,
/// μία φορά την ημέρα. Έτσι δεν μένουν μηχανήματα ανοιχτά όλη τη νύχτα.
/// </summary>
public class AutoShutdownScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<AutoShutdownScheduler> _logger;

    public AutoShutdownScheduler(IServiceScopeFactory scopes, ILogger<AutoShutdownScheduler> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Το αυτόματο κλείσιμο απέτυχε");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private async Task RunOnceAsync()
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var rooms = await db.Rooms.Where(r => r.AutoShutdown != null && r.AutoShutdown != "").ToListAsync();
        if (rooms.Count == 0)
        {
            return;
        }

        var now = DateTime.Now;

        foreach (var room in rooms)
        {
            if (!TimeOnly.TryParse(room.AutoShutdown, out var at))
            {
                continue;
            }

            // Έγινε ήδη σήμερα;
            if (room.AutoShutdownLast is { } last && last.Date == now.Date)
            {
                continue;
            }

            var scheduled = now.Date + at.ToTimeSpan();

            // Μέσα σε παράθυρο 2 ωρών μετά την ώρα (αν ο server άνοιξε αργότερα, εκτελείται μία φορά).
            if (now < scheduled || now > scheduled.AddHours(2))
            {
                continue;
            }

            var targets = await db.Computers
                .Where(c => !c.IsDynamic && !c.NoAutoShutdown
                            && c.Location != null && c.Location == room.Name
                            && c.IsOnline == true)
                .ToListAsync();

            foreach (var pc in targets)
            {
                await AgentCommands.QueueAsync(db, pc.Id, "shutdown",
                    by: "Αυτόματο κλείσιμο", delaySeconds: 60,
                    text: "Το εργαστήριο κλείνει. Ο υπολογιστής θα σβήσει σε 1 λεπτό.",
                    automatic: true);
            }

            room.AutoShutdownLast = now;
            await db.SaveChangesAsync();

            if (targets.Count > 0)
            {
                _logger.LogInformation("Αυτόματο κλείσιμο «{Room}»: {Count} υπολογιστές.", room.Name, targets.Count);
            }
        }
    }
}

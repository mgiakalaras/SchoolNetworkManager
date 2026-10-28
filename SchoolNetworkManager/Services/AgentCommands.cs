using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;

namespace SchoolNetworkManager.Services;

/// <summary>
/// Ουρά εντολών προς τους agents των υπολογιστών (κλείσιμο, επανεκκίνηση, μήνυμα κ.λπ.).
/// Ο agent ρωτάει τον server κάθε λίγα δευτερόλεπτα και εκτελεί ό,τι εκκρεμεί.
/// </summary>
public static class AgentCommands
{
    /// <summary>Οι εντολές που δεν παραλήφθηκαν σε αυτό το διάστημα θεωρούνται «χαμένες» (κλειστός υπολογιστής).</summary>
    public static readonly TimeSpan Expiry = TimeSpan.FromMinutes(10);

    private static readonly string[] Allowed =
        { "shutdown", "restart", "logoff", "cancel", "message", "lock", "unlock" };

    public static bool IsValidKind(string? kind) => kind != null && Allowed.Contains(kind);

    /// <summary>
    /// Προσθέτει μια εντολή για έναν υπολογιστή. Για shutdown/restart/logoff ακυρώνει
    /// πρώτα τυχόν προηγούμενη εκκρεμή εντολή ίδιου είδους, ώστε να μη στοιβάζονται.
    /// </summary>
    public static async Task<AgentCommand> QueueAsync(
        AppDbContext db, int computerId, string kind, string? by,
        int delaySeconds = 0, string? text = null, bool automatic = false)
    {
        if (kind is "shutdown" or "restart" or "logoff")
        {
            var stale = await db.AgentCommands
                .Where(c => c.ComputerId == computerId && c.Status == AgentCommand.Pending
                            && (c.Kind == "shutdown" || c.Kind == "restart" || c.Kind == "logoff"))
                .ToListAsync();

            foreach (var old in stale)
            {
                old.Status = AgentCommand.Cancelled;
            }
        }

        var cmd = new AgentCommand
        {
            ComputerId = computerId,
            Kind = kind,
            Text = Trim(text, 400),
            DelaySeconds = Math.Clamp(delaySeconds, 0, 3600),
            CreatedBy = Trim(by, 80),
            CreatedAt = DateTime.Now,
            IsAutomatic = automatic,
            Status = AgentCommand.Pending
        };

        db.AgentCommands.Add(cmd);
        await db.SaveChangesAsync();
        return cmd;
    }

    /// <summary>Οι εκκρεμείς εντολές ενός υπολογιστή· τις σημειώνει ως «στάλθηκαν».</summary>
    public static async Task<List<AgentCommand>> TakePendingAsync(AppDbContext db, int computerId)
    {
        await ExpireOldAsync(db);

        var pending = await db.AgentCommands
            .Where(c => c.ComputerId == computerId && c.Status == AgentCommand.Pending)
            .OrderBy(c => c.Id)
            .ToListAsync();

        var now = DateTime.Now;
        foreach (var c in pending)
        {
            c.Status = AgentCommand.Sent;
            c.SentAt = now;
        }

        if (pending.Count > 0)
        {
            await db.SaveChangesAsync();
        }

        return pending;
    }

    public static async Task CompleteAsync(AppDbContext db, int id, int computerId, bool ok, string? result)
    {
        var cmd = await db.AgentCommands.FirstOrDefaultAsync(c => c.Id == id && c.ComputerId == computerId);

        if (cmd == null || cmd.Status is AgentCommand.Done or AgentCommand.Failed)
        {
            return;
        }

        cmd.Status = ok ? AgentCommand.Done : AgentCommand.Failed;
        cmd.DoneAt = DateTime.Now;
        cmd.Result = Trim(result, 400);
        await db.SaveChangesAsync();
    }

    /// <summary>Παλιές εντολές που δεν παραλήφθηκαν ποτέ → «έληξε» (ο υπολογιστής ήταν μάλλον κλειστός).</summary>
    public static async Task ExpireOldAsync(AppDbContext db)
    {
        var cutoff = DateTime.Now - Expiry;

        var stale = await db.AgentCommands
            .Where(c => c.Status == AgentCommand.Pending && c.CreatedAt < cutoff)
            .ToListAsync();

        foreach (var c in stale)
        {
            c.Status = AgentCommand.Expired;
        }

        if (stale.Count > 0)
        {
            await db.SaveChangesAsync();
        }
    }

    private static string? Trim(string? v, int max)
        => string.IsNullOrWhiteSpace(v) ? null : (v.Length > max ? v[..max] : v).Trim();
}

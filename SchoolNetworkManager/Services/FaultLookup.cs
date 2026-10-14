using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;

namespace SchoolNetworkManager.Services;

public static class FaultLookup
{
    /// <summary>Συσκευές με ανοιχτή βλάβη → true αν είναι «Εκτός λειτουργίας».</summary>
    public static async Task<Dictionary<int, bool>> OpenByComputerAsync(AppDbContext db)
    {
        var open = await db.Faults
            .Where(f => f.ResolvedAt == null)
            .Select(f => new { f.ComputerId, f.Severity })
            .ToListAsync();

        return open
            .GroupBy(f => f.ComputerId)
            .ToDictionary(g => g.Key, g => g.Any(f => f.Severity == Fault.SeverityDown));
    }
}

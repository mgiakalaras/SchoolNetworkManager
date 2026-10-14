using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;

namespace SchoolNetworkManager.Services;

/// <summary>
/// «Ξεσκαρτάρισμα» διευθύνσεων: ποιες καταχωρήσεις είναι στην ουσία κενές διευθύνσεις DHCP.
/// </summary>
public static class AddressCleanup
{
    public enum Verdict
    {
        /// <summary>Σταθερή συσκευή — έχει στοιχεία που έγραψε άνθρωπος.</summary>
        Keep,

        /// <summary>Καμία χειροκίνητη πληροφορία: σχεδόν σίγουρα dynamic.</summary>
        Dynamic,

        /// <summary>Έχει μόνο αυτόματα στοιχεία (όνομα NetBIOS κ.λπ.) χωρίς χώρο — ρώτα τον χρήστη.</summary>
        Maybe
    }

    /// <summary>Συσκευές με ιστορικό που δείχνει ότι κάποιος ασχολήθηκε (βλάβες, κωδικοί, κάτοψη).</summary>
    public static async Task<HashSet<int>> TouchedAsync(AppDbContext db)
    {
        var ids = new HashSet<int>();

        ids.UnionWith(await db.Faults.Select(f => f.ComputerId).Distinct().ToListAsync());
        ids.UnionWith(await db.PasswordRecords.Where(p => p.ComputerId != null).Select(p => p.ComputerId!.Value).Distinct().ToListAsync());
        ids.UnionWith(await db.Seats.Select(s => s.ComputerId).Distinct().ToListAsync());

        return ids;
    }

    public static Verdict Judge(Computer c, HashSet<int> touched)
    {
        if (c.IsDynamic || c.IsEmptySlot)
        {
            return Verdict.Keep;
        }

        var handWritten =
            !string.IsNullOrWhiteSpace(c.Location) ||
            !string.IsNullOrWhiteSpace(c.Username) ||
            !string.IsNullOrWhiteSpace(c.Password) ||
            !string.IsNullOrWhiteSpace(c.Notes) ||
            !string.IsNullOrWhiteSpace(c.Extras) ||
            c.SpecValues.Count > 0 ||
            c.HasLanMessenger ||
            touched.Contains(c.Id) ||
            (c.MachineType != MachineTypes.Default && c.MachineType != "Άγνωστο");

        if (handWritten)
        {
            return Verdict.Keep;
        }

        // Μόνο ό,τι βρήκε η αυτόματη ανίχνευση (MAC/κατασκευαστής αλλάζουν σε κάθε DHCP).
        var nothingFound =
            string.IsNullOrWhiteSpace(c.Name) &&
            string.IsNullOrWhiteSpace(c.MacAddress) &&
            !c.HasVeyon;

        return nothingFound ? Verdict.Dynamic : Verdict.Maybe;
    }

    /// <summary>Σημειώνει ως dynamic και καθαρίζει ό,τι γέμισε η αυτόματη ανίχνευση.</summary>
    public static void MakeDynamic(Computer c, bool clearDetected, string? by)
    {
        c.IsDynamic = true;
        c.VeyonDown = false;
        c.VeyonDownSince = null;

        if (clearDetected)
        {
            c.MacAddress = null;
            c.DetectedMac = null;
            c.Vendor = null;
            c.OpenPorts = null;
            c.HasVeyon = false;
        }

        c.UpdatedAt = DateTime.Now;
        c.UpdatedBy = by;
    }
}

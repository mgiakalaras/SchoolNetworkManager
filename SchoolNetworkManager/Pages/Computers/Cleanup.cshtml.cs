using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;
using SchoolNetworkManager.Services;

namespace SchoolNetworkManager.Pages.Computers;

/// <summary>Ξεσκαρτάρισμα: ποιες διευθύνσεις έχουν σταθερή συσκευή και ποιες είναι dynamic (DHCP).</summary>
public class CleanupModel : PageModel
{
    private readonly AppDbContext _context;

    public CleanupModel(AppDbContext context)
    {
        _context = context;
    }

    public List<Computer> Sure { get; set; } = new();
    public List<Computer> Maybe { get; set; } = new();
    public List<Computer> Dynamic { get; set; } = new();
    public int StaticCount { get; set; }
    public int Total { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool ShowDynamic { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync()
    {
        var all = (await _context.Computers.AsNoTracking().ToListAsync())
            .OrderBy(c => NetworkTools.IpToNumber(c.IpAddress))
            .ToList();
        var touched = await AddressCleanup.TouchedAsync(_context);

        Total = all.Count;
        Dynamic = all.Where(c => c.IsDynamic).ToList();
        Sure = all.Where(c => AddressCleanup.Judge(c, touched) == AddressCleanup.Verdict.Dynamic).ToList();
        Maybe = all.Where(c => AddressCleanup.Judge(c, touched) == AddressCleanup.Verdict.Maybe).ToList();
        StaticCount = Total - Dynamic.Count;
    }

    public async Task<IActionResult> OnPostDynamicAsync(int[] ids, bool clearDetected = true)
    {
        var set = ids.ToHashSet();
        var computers = await _context.Computers.Where(c => set.Contains(c.Id)).ToListAsync();
        var by = UserPasswords.ActorName(User);

        foreach (var computer in computers)
        {
            AddressCleanup.MakeDynamic(computer, clearDetected, by);
        }

        await _context.SaveChangesAsync();

        StatusMessage = computers.Count == 0
            ? "Δεν επιλέχθηκε καμία διεύθυνση."
            : $"🔄 {computers.Count} διευθύνσεις έγιναν Dynamic.";

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostStaticAsync(int[] ids)
    {
        var set = ids.ToHashSet();
        var computers = await _context.Computers.Where(c => set.Contains(c.Id)).ToListAsync();
        var by = UserPasswords.ActorName(User);

        foreach (var computer in computers)
        {
            computer.IsDynamic = false;
            computer.UpdatedAt = DateTime.Now;
            computer.UpdatedBy = by;
        }

        await _context.SaveChangesAsync();

        StatusMessage = $"📌 {computers.Count} διευθύνσεις έγιναν ξανά Static.";

        return RedirectToPage(new { ShowDynamic = true });
    }

    /// <summary>Όλο το εύρος DHCP του router → Dynamic, εκτός από όσες έχουν στοιχεία συσκευής.</summary>
    public async Task<IActionResult> OnPostRangeAsync(string? range, bool clearDetected = true)
    {
        var ips = NetworkTools.ParseRange(range).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (ips.Count == 0)
        {
            StatusMessage = "⚠️ Γράψε εύρος, π.χ. 192.168.2.100-200.";
            return RedirectToPage();
        }

        var touched = await AddressCleanup.TouchedAsync(_context);
        var computers = (await _context.Computers.ToListAsync())
            .Where(c => ips.Contains(c.IpAddress.Trim()) && !c.IsDynamic)
            .ToList();
        var by = UserPasswords.ActorName(User);

        var changed = 0;
        var kept = new List<string>();

        foreach (var computer in computers)
        {
            if (AddressCleanup.Judge(computer, touched) == AddressCleanup.Verdict.Keep && !computer.IsEmptySlot)
            {
                kept.Add(string.IsNullOrWhiteSpace(computer.Name) ? computer.IpAddress : $"{computer.Name} ({computer.IpAddress})");
                continue;
            }

            AddressCleanup.MakeDynamic(computer, clearDetected, by);
            changed++;
        }

        await _context.SaveChangesAsync();

        StatusMessage = $"🔄 {changed} διευθύνσεις του εύρους έγιναν Dynamic." +
                        (kept.Count > 0
                            ? $" Έμειναν Static γιατί έχουν στοιχεία συσκευής: {string.Join(", ", kept.Take(10))}{(kept.Count > 10 ? "…" : "")}"
                            : string.Empty);

        return RedirectToPage();
    }
}

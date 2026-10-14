using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;

namespace SchoolNetworkManager.Pages.Consumables;

public class IndexModel : PageModel
{
    private readonly AppDbContext _context;

    public IndexModel(AppDbContext context)
    {
        _context = context;
    }

    public List<Consumable> Items { get; set; } = new();

    public List<ConsumableLog> RecentLogs { get; set; } = new();

    public Dictionary<int, string> NamesById { get; set; } = new();

    [BindProperty]
    public Consumable Edit { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? Category { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync()
    {
        var query = _context.Consumables.AsQueryable();

        if (!string.IsNullOrWhiteSpace(Category))
        {
            query = query.Where(c => c.Category == Category);
        }

        Items = (await query.ToListAsync())
            .OrderByDescending(c => c.IsOut)
            .ThenByDescending(c => c.IsLow)
            .ThenBy(c => c.Category)
            .ThenBy(c => c.Name)
            .ToList();

        RecentLogs = await _context.ConsumableLogs
            .OrderByDescending(l => l.At)
            .Take(15)
            .ToListAsync();

        NamesById = await _context.Consumables.ToDictionaryAsync(c => c.Id, c => c.Name);
    }

    public async Task<IActionResult> OnPostSaveAsync(string? by)
    {
        by = SchoolNetworkManager.Services.UserPasswords.ActorName(User, by);

        if (string.IsNullOrWhiteSpace(Edit.Name))
        {
            StatusMessage = "⚠️ Γράψε όνομα αναλωσίμου.";
            return RedirectToPage();
        }

        Consumable item;

        if (Edit.Id > 0)
        {
            item = await _context.Consumables.FindAsync(Edit.Id) ?? new Consumable();

            if (item.Id == 0)
            {
                return NotFound();
            }
        }
        else
        {
            item = new Consumable { CreatedAt = DateTime.Now };
            _context.Consumables.Add(item);
        }

        var before = item.Quantity;

        item.Name = Edit.Name.Trim();
        item.Category = Consumable.Categories.Contains(Edit.Category) ? Edit.Category : "Άλλο";
        item.CompatibleWith = Clean(Edit.CompatibleWith);
        item.Quantity = Math.Max(0, Edit.Quantity);
        item.MinQuantity = Math.Max(0, Edit.MinQuantity);
        item.StoredAt = Clean(Edit.StoredAt);
        item.Notes = Clean(Edit.Notes);
        item.UpdatedAt = DateTime.Now;

        await _context.SaveChangesAsync();

        if (item.Quantity != before)
        {
            _context.ConsumableLogs.Add(new ConsumableLog
            {
                ConsumableId = item.Id,
                Change = item.Quantity - before,
                QuantityAfter = item.Quantity,
                Note = Edit.Id > 0 ? "Διόρθωση αποθέματος" : "Αρχικό απόθεμα",
                By = Clean(by),
                At = DateTime.Now
            });
            await _context.SaveChangesAsync();
        }

        StatusMessage = $"✓ Αποθηκεύτηκε: {item.Name}.";

        return RedirectToPage(new { Category });
    }

    /// <summary>Γρήγορη κίνηση: +1 παραλαβή / −1 χρήση.</summary>
    public async Task<IActionResult> OnPostMoveAsync(int id, int change, string? note, string? by)
    {
        by = SchoolNetworkManager.Services.UserPasswords.ActorName(User, by);

        var item = await _context.Consumables.FindAsync(id);

        if (item == null)
        {
            return NotFound();
        }

        if (change == 0)
        {
            return RedirectToPage(new { Category });
        }

        item.Quantity = Math.Max(0, item.Quantity + change);
        item.UpdatedAt = DateTime.Now;

        _context.ConsumableLogs.Add(new ConsumableLog
        {
            ConsumableId = item.Id,
            Change = change,
            QuantityAfter = item.Quantity,
            Note = Clean(note) ?? (change > 0 ? "Παραλαβή" : "Χρήση"),
            By = Clean(by),
            At = DateTime.Now
        });

        await _context.SaveChangesAsync();

        StatusMessage = item.IsOut
            ? $"⛔ {item.Name}: τελείωσε!"
            : item.IsLow
                ? $"⚠️ {item.Name}: έμειναν {item.Quantity} — ώρα για παραγγελία."
                : $"✓ {item.Name}: απόθεμα {item.Quantity}.";

        return RedirectToPage(new { Category });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var item = await _context.Consumables.FindAsync(id);

        if (item != null)
        {
            _context.Consumables.Remove(item);
            await _context.SaveChangesAsync();
            await _context.ConsumableLogs.Where(l => l.ConsumableId == id).ExecuteDeleteAsync();
            StatusMessage = $"🗑️ Διαγράφηκε: {item.Name}.";
        }

        return RedirectToPage(new { Category });
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

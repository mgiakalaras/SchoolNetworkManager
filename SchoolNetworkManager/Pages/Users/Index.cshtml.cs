using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;
using SchoolNetworkManager.Services;

namespace SchoolNetworkManager.Pages.Users;

/// <summary>Χρήστες της εφαρμογής (μόνο για διαχειριστές). Τα δεδομένα είναι κοινά για όλους.</summary>
public class IndexModel : PageModel
{
    private readonly AppDbContext _context;

    public IndexModel(AppDbContext context)
    {
        _context = context;
    }

    public List<AppUser> Users { get; set; } = new();

    [BindProperty] public string? NewUserName { get; set; }
    [BindProperty] public string? NewDisplayName { get; set; }
    [BindProperty] public string? NewPassword { get; set; }
    [BindProperty] public bool NewIsAdmin { get; set; }

    [TempData] public string? StatusMessage { get; set; }
    [TempData] public string? StatusKind { get; set; }

    /// <summary>Προσωρινός κωδικός που εμφανίζεται ΜΙΑ φορά μετά τη δημιουργία / επαναφορά.</summary>
    [TempData] public string? IssuedFor { get; set; }
    [TempData] public string? IssuedUser { get; set; }
    [TempData] public string? IssuedPassword { get; set; }

    public int? MyId => UserPasswords.UserId(User);

    public async Task OnGetAsync()
    {
        Users = await _context.Users
            .OrderBy(u => u.IsDisabled)
            .ThenBy(u => u.DisplayName)
            .ToListAsync();
    }

    private static string TempPassword() => PasswordGenerator.Generate(new PasswordOptions
    {
        Length = 10,
        Symbols = false,
        AvoidAmbiguous = true
    });

    public async Task<IActionResult> OnPostCreateAsync()
    {
        var userName = (NewUserName ?? string.Empty).Trim().ToLowerInvariant();
        var displayName = (NewDisplayName ?? string.Empty).Trim();

        if (!Regex.IsMatch(userName, "^[a-z0-9._-]{2,40}$"))
        {
            return Fail("Όνομα χρήστη: 2–40 λατινικά γράμματα, αριθμοί, τελεία ή παύλα (π.χ. mgiakalaras).");
        }

        if (userName == UserPasswords.MasterUserName)
        {
            return Fail("Το «admin» είναι ο κοινός λογαριασμός του server — διάλεξε άλλο όνομα.");
        }

        if (displayName.Length == 0 || displayName.Length > 80)
        {
            return Fail("Γράψε ονοματεπώνυμο.");
        }

        if (await _context.Users.AnyAsync(u => u.UserName == userName))
        {
            return Fail($"Υπάρχει ήδη χρήστης «{userName}».");
        }

        var password = string.IsNullOrWhiteSpace(NewPassword) ? TempPassword() : NewPassword.Trim();

        if (UserPasswords.Problem(password) is { } problem)
        {
            return Fail(problem);
        }

        var account = new AppUser
        {
            UserName = userName,
            DisplayName = displayName,
            PasswordHash = UserPasswords.Hash(password),
            IsAdmin = NewIsAdmin,
            MustChangePassword = true,
            CreatedAt = DateTime.Now
        };

        _context.Users.Add(account);
        await _context.SaveChangesAsync();

        IssuedFor = displayName;
        IssuedUser = userName;
        IssuedPassword = password;
        StatusKind = "success";
        StatusMessage = $"Ο χρήστης «{displayName}» δημιουργήθηκε.";

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostResetAsync(int id)
    {
        var account = await _context.Users.FindAsync(id);

        if (account == null)
        {
            return RedirectToPage();
        }

        var password = TempPassword();
        account.PasswordHash = UserPasswords.Hash(password);
        account.MustChangePassword = true;
        account.SecurityStamp = Guid.NewGuid().ToString("N");
        await _context.SaveChangesAsync();

        IssuedFor = account.DisplayName;
        IssuedUser = account.UserName;
        IssuedPassword = password;
        StatusKind = "success";
        StatusMessage = $"Νέος προσωρινός κωδικός για «{account.DisplayName}». Οι ανοιχτές συνδέσεις του έκλεισαν.";

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleDisabledAsync(int id)
    {
        if (id == MyId)
        {
            return Fail("Δεν μπορείς να απενεργοποιήσεις τον εαυτό σου.");
        }

        var account = await _context.Users.FindAsync(id);

        if (account != null)
        {
            account.IsDisabled = !account.IsDisabled;
            account.SecurityStamp = Guid.NewGuid().ToString("N");
            await _context.SaveChangesAsync();

            StatusKind = "success";
            StatusMessage = account.IsDisabled
                ? $"⏸ Ο «{account.DisplayName}» απενεργοποιήθηκε και αποσυνδέθηκε."
                : $"▶ Ο «{account.DisplayName}» ενεργοποιήθηκε ξανά.";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAdminAsync(int id)
    {
        if (id == MyId)
        {
            return Fail("Δεν μπορείς να αλλάξεις τον δικό σου ρόλο — ζήτα το από άλλον διαχειριστή.");
        }

        var account = await _context.Users.FindAsync(id);

        if (account != null)
        {
            account.IsAdmin = !account.IsAdmin;
            account.SecurityStamp = Guid.NewGuid().ToString("N");
            await _context.SaveChangesAsync();

            StatusKind = "success";
            StatusMessage = account.IsAdmin
                ? $"⭐ Ο «{account.DisplayName}» έγινε διαχειριστής."
                : $"Ο «{account.DisplayName}» δεν είναι πλέον διαχειριστής.";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        if (id == MyId)
        {
            return Fail("Δεν μπορείς να διαγράψεις τον εαυτό σου.");
        }

        var account = await _context.Users.FindAsync(id);

        if (account != null)
        {
            _context.Users.Remove(account);
            await _context.SaveChangesAsync();

            StatusKind = "success";
            StatusMessage = $"🗑️ Ο χρήστης «{account.DisplayName}» διαγράφηκε. Ό,τι έχει καταγράψει (βλάβες, κωδικοί…) μένει με το όνομά του.";
        }

        return RedirectToPage();
    }

    private IActionResult Fail(string message)
    {
        StatusKind = "danger";
        StatusMessage = "⚠️ " + message;
        return RedirectToPage();
    }
}

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;
using SchoolNetworkManager.Services;

namespace SchoolNetworkManager.Pages;

/// <summary>Ο λογαριασμός μου: όνομα και αλλαγή κωδικού.</summary>
public class AccountModel : PageModel
{
    private readonly AppDbContext _context;

    public AccountModel(AppDbContext context)
    {
        _context = context;
    }

    [BindProperty(SupportsGet = true)]
    public bool First { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public AppUser? Account { get; set; }

    public bool IsMaster => UserPasswords.UserId(User) == null;

    [BindProperty] public string? DisplayName { get; set; }
    [BindProperty] public string? CurrentPassword { get; set; }
    [BindProperty] public string? NewPassword { get; set; }
    [BindProperty] public string? ConfirmPassword { get; set; }

    public string? Error { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    /// <summary>Ήρθε εδώ επειδή δεν είχε δικαίωμα (σελίδα μόνο για διαχειριστές).</summary>
    public bool Denied => !string.IsNullOrEmpty(ReturnUrl);

    private async Task<bool> LoadAsync()
    {
        var id = UserPasswords.UserId(User);

        if (id == null)
        {
            return false;
        }

        Account = await _context.Users.FindAsync(id.Value);

        return Account != null;
    }

    public async Task OnGetAsync()
    {
        await LoadAsync();
        DisplayName = Account?.DisplayName;
    }

    public async Task<IActionResult> OnPostProfileAsync()
    {
        if (!await LoadAsync())
        {
            return RedirectToPage();
        }

        var name = DisplayName?.Trim();

        if (string.IsNullOrEmpty(name) || name.Length > 80)
        {
            Error = "Γράψε ονοματεπώνυμο (έως 80 χαρακτήρες).";
            return Page();
        }

        Account!.DisplayName = name;
        await _context.SaveChangesAsync();
        await RefreshSignInAsync(Account);

        StatusMessage = "✓ Το όνομα αποθηκεύτηκε.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPasswordAsync()
    {
        if (!await LoadAsync())
        {
            return RedirectToPage();
        }

        DisplayName = Account!.DisplayName;

        if (!Account.MustChangePassword && !UserPasswords.Verify(CurrentPassword ?? string.Empty, Account.PasswordHash))
        {
            await Task.Delay(800);
            Error = "Ο τρέχων κωδικός δεν είναι σωστός.";
            return Page();
        }

        if (UserPasswords.Problem(NewPassword) is { } problem)
        {
            Error = problem;
            return Page();
        }

        if (NewPassword != ConfirmPassword)
        {
            Error = "Οι δύο νέοι κωδικοί δεν είναι ίδιοι.";
            return Page();
        }

        if (UserPasswords.MatchesMaster(NewPassword!))
        {
            Error = "Μην βάζεις τον κοινό κωδικό του server — διάλεξε δικό σου.";
            return Page();
        }

        Account.PasswordHash = UserPasswords.Hash(NewPassword!);
        Account.MustChangePassword = false;
        Account.SecurityStamp = Guid.NewGuid().ToString("N");   // κλείνουν οι συνδέσεις σε άλλους browsers
        await _context.SaveChangesAsync();
        await RefreshSignInAsync(Account);

        StatusMessage = "🔒 Ο κωδικός άλλαξε. Οι άλλες συνδέσεις σου αποσυνδέθηκαν.";
        return First ? RedirectToPage("/Index") : RedirectToPage();
    }

    private Task RefreshSignInAsync(AppUser account) =>
        HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            UserPasswords.Principal(account),
            new AuthenticationProperties { IsPersistent = true });
}

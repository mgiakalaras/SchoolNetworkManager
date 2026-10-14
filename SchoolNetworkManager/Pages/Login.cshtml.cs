using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Services;

namespace SchoolNetworkManager.Pages;

public class LoginModel : PageModel
{
    private readonly AppDbContext _context;

    public LoginModel(AppDbContext context)
    {
        _context = context;
    }

    [BindProperty]
    public string? UserName { get; set; }

    [BindProperty]
    public string? Password { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; set; }

    public bool HasUsers { get; set; }

    private static bool AuthEnabled =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ADMIN_PASSWORD"));

    public async Task<IActionResult> OnGetAsync()
    {
        // Αν δεν έχει οριστεί κωδικός ή ο χρήστης έχει ήδη συνδεθεί, πάμε στην αρχική.
        if (!AuthEnabled || User.Identity?.IsAuthenticated == true)
        {
            return RedirectToPage("/Index");
        }

        HasUsers = await _context.Users.AnyAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!AuthEnabled)
        {
            return RedirectToPage("/Index");
        }

        HasUsers = await _context.Users.AnyAsync();

        var name = (UserName ?? string.Empty).Trim().ToLowerInvariant();
        var given = Password ?? string.Empty;

        System.Security.Claims.ClaimsPrincipal? principal = null;
        var mustChange = false;

        if (name.Length == 0 || name == UserPasswords.MasterUserName)
        {
            // Κοινός λογαριασμός: ο κωδικός ADMIN_PASSWORD του stack.
            if (UserPasswords.MatchesMaster(given))
            {
                principal = UserPasswords.Principal(null);
            }
        }
        else
        {
            var account = await _context.Users.FirstOrDefaultAsync(u => u.UserName == name);

            if (account != null && !account.IsDisabled && UserPasswords.Verify(given, account.PasswordHash))
            {
                account.LastLoginAt = DateTime.Now;
                await _context.SaveChangesAsync();

                principal = UserPasswords.Principal(account);
                mustChange = account.MustChangePassword;
            }
        }

        if (principal == null)
        {
            // Μικρή καθυστέρηση ώστε να μη δοκιμάζονται κωδικοί μαζικά.
            await Task.Delay(1000);
            ErrorMessage = "Λάθος όνομα χρήστη ή κωδικός.";
            Password = null;
            return Page();
        }

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties { IsPersistent = true });

        if (mustChange)
        {
            return RedirectToPage("/Account", new { first = true });
        }

        if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
        {
            return LocalRedirect(ReturnUrl);
        }

        return RedirectToPage("/Index");
    }
}

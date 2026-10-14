using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace SchoolNetworkManager.Services;

/// <summary>Κατακερματισμός κωδικών χρηστών (PBKDF2-SHA256) και βοηθητικά για τους λογαριασμούς.</summary>
public static class UserPasswords
{
    private const int Iterations = 120_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public const int MinLength = 8;

    /// <summary>Ο λογαριασμός «admin» με τον κωδικό ADMIN_PASSWORD του stack (πάντα διαχειριστής).</summary>
    public const string MasterUserName = "admin";

    public const string ClaimDisplayName = "snm:display";
    public const string ClaimStamp = "snm:stamp";
    public const string ClaimUserId = "snm:uid";
    public const string ClaimMustChange = "snm:mustchange";
    public const string RoleAdmin = "admin";

    /// <summary>Ταυτότητα για το cookie σύνδεσης.</summary>
    public static ClaimsPrincipal Principal(SchoolNetworkManager.Models.AppUser? user)
    {
        var claims = new List<Claim>();

        if (user == null)
        {
            claims.Add(new Claim(ClaimTypes.Name, MasterUserName));
            claims.Add(new Claim(ClaimDisplayName, "Διαχειριστής (κοινός)"));
            claims.Add(new Claim(ClaimTypes.Role, RoleAdmin));
        }
        else
        {
            claims.Add(new Claim(ClaimTypes.Name, user.UserName));
            claims.Add(new Claim(ClaimDisplayName, user.DisplayName));
            claims.Add(new Claim(ClaimUserId, user.Id.ToString()));
            claims.Add(new Claim(ClaimStamp, user.SecurityStamp));

            if (user.IsAdmin)
            {
                claims.Add(new Claim(ClaimTypes.Role, RoleAdmin));
            }

            if (user.MustChangePassword)
            {
                claims.Add(new Claim(ClaimMustChange, "1"));
            }
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme));
    }

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, HashSize);

        return $"pbkdf2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string? stored)
    {
        if (string.IsNullOrEmpty(stored))
        {
            return false;
        }

        var parts = stored.Split('$');

        if (parts.Length != 4 || parts[0] != "pbkdf2" || !int.TryParse(parts[1], out var iterations))
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, expected.Length);

            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static bool MatchesMaster(string password)
    {
        var master = Environment.GetEnvironmentVariable("ADMIN_PASSWORD");

        if (string.IsNullOrWhiteSpace(master))
        {
            return false;
        }

        // Σύγκριση σταθερού χρόνου: κατακερματίζουμε πρώτα για ίδιο μήκος.
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(master));

        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    public static string? Problem(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
        {
            return $"Ο κωδικός θέλει τουλάχιστον {MinLength} χαρακτήρες.";
        }

        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
        {
            return "Ο κωδικός θέλει και γράμματα και αριθμούς.";
        }

        return null;
    }

    // ---------- Στοιχεία του συνδεδεμένου χρήστη ----------

    /// <summary>Το όνομα που μπαίνει στα «ποιος το έκανε» (βλάβες, κωδικοί, αναλώσιμα).</summary>
    public static string? DisplayName(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var display = user.FindFirst(ClaimDisplayName)?.Value;

        return string.IsNullOrWhiteSpace(display) ? user.Identity.Name : display;
    }

    /// <summary>
    /// «Ποιος το έκανε»: για συνδεδεμένο χρήστη πάντα το δικό του όνομα (δεν αλλάζει από τη φόρμα)·
    /// για τον κοινό λογαριασμό admin ό,τι γράφτηκε στη φόρμα.
    /// </summary>
    public static string? ActorName(ClaimsPrincipal? user, string? typed = null)
    {
        if (UserId(user) != null && DisplayName(user) is { Length: > 0 } name)
        {
            return name;
        }

        if (!string.IsNullOrWhiteSpace(typed))
        {
            return typed.Trim();
        }

        return user?.Identity?.IsAuthenticated == true ? user.Identity.Name : null;
    }

    /// <summary>Σταθερό χρώμα (0–359) για το «αβατάρ» κάθε χρήστη.</summary>
    public static int Hue(string? text)
    {
        var hash = 17;

        foreach (var ch in text ?? string.Empty)
        {
            hash = unchecked(hash * 31 + ch);
        }

        return Math.Abs(hash % 360);
    }

    public static bool IsAdmin(ClaimsPrincipal? user) =>
        user?.Identity?.IsAuthenticated == true && user.IsInRole(RoleAdmin);

    public static int? UserId(ClaimsPrincipal? user) =>
        int.TryParse(user?.FindFirst(ClaimUserId)?.Value, out var id) ? id : null;
}

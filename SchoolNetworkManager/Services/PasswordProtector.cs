using Microsoft.AspNetCore.DataProtection;

namespace SchoolNetworkManager.Services;

/// <summary>
/// Κρυπτογραφεί τους κωδικούς των συσκευών πριν γραφτούν στη βάση.
/// Τα κλειδιά βρίσκονται στον φάκελο δεδομένων (data/keys) — κράτα backup όλο τον φάκελο.
/// </summary>
public static class PasswordProtector
{
    private const string Prefix = "enc:";
    private static IDataProtector? _protector;

    public static void Initialize(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("SchoolNetworkManager.DevicePasswords");
    }

    public static string? Protect(string? value)
    {
        if (string.IsNullOrEmpty(value) ||
            _protector == null ||
            value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return value;
        }

        return Prefix + _protector.Protect(value);
    }

    public static string? Unprotect(string? value)
    {
        if (string.IsNullOrEmpty(value) ||
            _protector == null ||
            !value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return value;
        }

        try
        {
            return _protector.Unprotect(value.Substring(Prefix.Length));
        }
        catch
        {
            // Αν λείπει το κλειδί, κρατάμε την κρυπτογραφημένη τιμή ώστε να μη χαθεί.
            return value;
        }
    }
}

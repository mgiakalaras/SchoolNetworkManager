using System.ComponentModel.DataAnnotations;

namespace SchoolNetworkManager.Models;

/// <summary>
/// Χρήστης της εφαρμογής (συνάδελφος πληροφορικός). Όλοι βλέπουν τα ίδια δεδομένα·
/// ο καθένας μπαίνει με δικό του κωδικό ώστε να φαίνεται ποιος έκανε τι.
/// </summary>
public class AppUser
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Γράψε όνομα χρήστη.")]
    [MaxLength(40)]
    [RegularExpression(@"^[A-Za-z0-9._-]+$", ErrorMessage = "Μόνο λατινικά γράμματα, αριθμοί, τελεία, παύλα.")]
    [Display(Name = "Όνομα χρήστη")]
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Γράψε ονοματεπώνυμο.")]
    [MaxLength(80)]
    [Display(Name = "Ονοματεπώνυμο")]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>PBKDF2 — βλ. Services/UserPasswords.cs. Ποτέ ο ίδιος ο κωδικός.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Διαχειριστής: προσθέτει / αφαιρεί χρήστες.</summary>
    [Display(Name = "Διαχειριστής")]
    public bool IsAdmin { get; set; }

    [Display(Name = "Απενεργοποιημένος")]
    public bool IsDisabled { get; set; }

    /// <summary>Αλλάζει σε κάθε αλλαγή κωδικού / απενεργοποίηση ώστε να κλείνουν οι ανοιχτές συνδέσεις.</summary>
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Πρέπει να αλλάξει τον κωδικό στην επόμενη σύνδεση (αρχικός κωδικός από τον διαχειριστή).</summary>
    public bool MustChangePassword { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? LastLoginAt { get; set; }

    public string Initials
    {
        get
        {
            var parts = DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length switch
            {
                0 => UserName.Length > 0 ? UserName[..1].ToUpperInvariant() : "?",
                1 => parts[0][..1].ToUpperInvariant(),
                _ => (parts[0][..1] + parts[^1][..1]).ToUpperInvariant()
            };
        }
    }
}

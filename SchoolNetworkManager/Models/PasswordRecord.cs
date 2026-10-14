namespace SchoolNetworkManager.Models;

/// <summary>
/// Ιστορικό κωδικών: ποιος κωδικός μπήκε, σε ποιον λογαριασμό, σε ποια συσκευή (ή άλλο στόχο),
/// από ποιον και πότε. Ο κωδικός αποθηκεύεται κρυπτογραφημένος.
/// </summary>
public class PasswordRecord
{
    public static readonly string[] CommonAccounts =
    {
        "Admin (τοπικός διαχειριστής)", "Μαθητής", "Καθηγητής", "BIOS / UEFI", "Wi-Fi", "Router / Switch", "Εκτυπωτής", "Άλλο"
    };

    public int Id { get; set; }
    public string Account { get; set; } = string.Empty;
    public int? ComputerId { get; set; }
    public string? Target { get; set; }
    public string? Secret { get; set; }
    public string? SetBy { get; set; }
    public DateTime SetAt { get; set; } = DateTime.Now;
    public string? Note { get; set; }

    /// <summary>Ομάδα καταχώρησης (ίδια για όσους κωδικούς μπήκαν μαζί).</summary>
    public string? BatchId { get; set; }
}

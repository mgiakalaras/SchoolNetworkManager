using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SchoolNetworkManager.Models;

public class Computer
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Η IP Address είναι υποχρεωτική.")]
    [Display(Name = "IP Address")]
    public string IpAddress { get; set; } = string.Empty;

    [Display(Name = "Τύπος Μηχανήματος")]
    public string MachineType { get; set; } = MachineTypes.Default;

    [Display(Name = "Name")]
    public string? Name { get; set; }

    [Display(Name = "MAC Address")]
    public string? MacAddress { get; set; }

    [Display(Name = "Location")]
    public string? Location { get; set; }

    [Display(Name = "Username")]
    public string? Username { get; set; }

    [Display(Name = "Password")]
    public string? Password { get; set; }

    [Display(Name = "Επιπλέον πληροφορίες")]
    public string? Extras { get; set; }

    [Display(Name = "Σημειώσεις")]
    public string? Notes { get; set; }

    // Παλιό δεύτερο πεδίο σημειώσεων — πλέον ενώνεται αυτόματα με τις «Σημειώσεις».
    public string? Comments { get; set; }

    /// <summary>
    /// Dynamic: διεύθυνση του DHCP χωρίς σταθερή συσκευή (μπαίνει ό,τι συνδεθεί).
    /// Δεν μετράει ως συσκευή σε στατιστικά, χώρους, αναφορές και εξαγωγές.
    /// </summary>
    [Display(Name = "Dynamic (DHCP)")]
    public bool IsDynamic { get; set; }

    // Φύλακας Veyon: ο υπολογιστής ήταν ανοιχτός αλλά η υπηρεσία Veyon δεν απαντούσε.
    public bool VeyonDown { get; set; }
    public DateTime? VeyonDownSince { get; set; }

    // Χαρακτηριστικά ανά τύπο (JSON) — βλ. Models/DeviceSpecs.cs
    public string? Specs { get; set; }

    [NotMapped]
    public Dictionary<string, string> SpecValues => DeviceSpecs.Parse(Specs);

    public static string? MergeNotes(string? first, string? second)
    {
        var a = first?.Trim();
        var b = second?.Trim();

        if (string.IsNullOrEmpty(b) || string.Equals(a, b, StringComparison.Ordinal)) return string.IsNullOrEmpty(a) ? null : a;
        if (string.IsNullOrEmpty(a)) return b;
        if (a.Contains(b, StringComparison.Ordinal)) return a;

        return a + Environment.NewLine + b;
    }

    [Display(Name = "LAN Messenger")]
    public bool HasLanMessenger { get; set; }

    [Display(Name = "Veyon")]
    public bool HasVeyon { get; set; }

    [Display(Name = "Online")]
    public bool? IsOnline { get; set; }

    public DateTime? LastCheckedAt { get; set; }

    [Display(Name = "Τελευταία φορά online")]
    public DateTime? LastSeenOnline { get; set; }

    [Display(Name = "Κατασκευαστής")]
    public string? Vendor { get; set; }

    [Display(Name = "Ανοιχτά ports")]
    public string? OpenPorts { get; set; }

    // MAC που βρέθηκε στο δίκτυο και διαφέρει από την καταχωρημένη (πιθανή αλλαγή συσκευής).
    public string? DetectedMac { get; set; }

    [NotMapped]
    public bool HasMacMismatch =>
        !string.IsNullOrWhiteSpace(DetectedMac) &&
        !string.IsNullOrWhiteSpace(MacAddress) &&
        !string.Equals(
            SchoolNetworkManager.Services.NetworkTools.NormalizeMac(DetectedMac),
            SchoolNetworkManager.Services.NetworkTools.NormalizeMac(MacAddress),
            StringComparison.OrdinalIgnoreCase);

    [NotMapped]
    public List<string> OpenPortList =>
        string.IsNullOrWhiteSpace(OpenPorts)
            ? new List<string>()
            : OpenPorts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? UpdatedAt { get; set; }

    /// <summary>Ποιος χρήστης έκανε την τελευταία αλλαγή.</summary>
    public string? UpdatedBy { get; set; }
    [NotMapped]
    public bool IsEmptySlot =>
        IsDynamic ||
        (!string.IsNullOrWhiteSpace(Name) &&
         Name.Trim().Contains("EMPTY", StringComparison.OrdinalIgnoreCase));

    [NotMapped]
    public string DisplayMachineType =>
        IsDynamic ? "Dynamic" :
        IsEmptySlot ? "-" :
        string.IsNullOrWhiteSpace(MachineType) ? MachineTypes.Default : MachineType;
}
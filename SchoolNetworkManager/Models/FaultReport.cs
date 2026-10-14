using System.ComponentModel.DataAnnotations;

namespace SchoolNetworkManager.Models;

/// <summary>
/// Αναφορά από τη δημόσια φόρμα (μαθητής / εκπαιδευτικός χωρίς λογαριασμό).
/// Μπαίνει στο βιβλίο βλαβών μόνο όταν την αποδεχτεί κάποιος της ομάδας.
/// </summary>
public class FaultReport
{
    public const string StatusNew = "new";
    public const string StatusAccepted = "accepted";
    public const string StatusRejected = "rejected";

    public static readonly string[] Roles = { "Μαθητής/τρια", "Εκπαιδευτικός", "Άλλο" };

    public int Id { get; set; }

    public string? Location { get; set; }

    public int? ComputerId { get; set; }

    /// <summary>Αν δεν βρήκε τη συσκευή στη λίστα: τι είναι (π.χ. «projector», «θέση δίπλα στο παράθυρο»).</summary>
    [MaxLength(120)]
    public string? DeviceText { get; set; }

    [MaxLength(1000)]
    public string Description { get; set; } = string.Empty;

    public string Severity { get; set; } = Fault.SeverityMinor;

    [MaxLength(80)]
    public string? ReporterName { get; set; }

    [MaxLength(40)]
    public string? ReporterRole { get; set; }

    [MaxLength(40)]
    public string? ClassName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [MaxLength(64)]
    public string? IpAddress { get; set; }

    public string Status { get; set; } = StatusNew;

    public string? HandledBy { get; set; }

    public DateTime? HandledAt { get; set; }

    public int? FaultId { get; set; }

    public string ReporterLabel =>
        string.Join(" · ", new[] { ReporterName, ReporterRole, ClassName }.Where(x => !string.IsNullOrWhiteSpace(x)));
}

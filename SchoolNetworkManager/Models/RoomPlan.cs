namespace SchoolNetworkManager.Models;

/// <summary>
/// Στοιχείο κάτοψης χώρου: είτε συσκευή (ComputerId) είτε ελεύθερο σχήμα / ετικέτα
/// (πόρτα, πίνακας, έδρα, παράθυρο…). Οι θέσεις είναι σε ποσοστά (0–100) του σχεδίου.
/// </summary>
public class PlanItem
{
    public int Id { get; set; }
    public string Location { get; set; } = string.Empty;
    public int? ComputerId { get; set; }
    public string? Label { get; set; }
    public string Kind { get; set; } = "device";
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }
}

/// <summary>Ρυθμίσεις κάτοψης ανά χώρο (αναλογία, εικόνα φόντου).</summary>
public class RoomPlan
{
    public int Id { get; set; }
    public string Location { get; set; } = string.Empty;
    public double AspectRatio { get; set; } = 1.6;
    public string? BackgroundPath { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Τμήμα που χρησιμοποιεί ένα εργαστήριο (π.χ. «Β1 Πληροφορικής»).</summary>
public class SchoolClass
{
    public int Id { get; set; }
    public string Location { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Teacher { get; set; }
    public string? Schedule { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>Ποιος μαθητής κάθεται σε ποιον υπολογιστή, για ένα τμήμα.</summary>
public class Seat
{
    public int Id { get; set; }
    public int SchoolClassId { get; set; }
    public int ComputerId { get; set; }
    public string StudentName { get; set; } = string.Empty;
}
